using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Application.Subscriptions.Queries;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;
using System.Net;
using System.Text.RegularExpressions;

namespace Application.Subscriptions.Commands;

public sealed record UpdateUserSubscriptionStatusCommand(
    Guid UserSubscriptionId,
    string Status,
    string Reason) : IRequest<Result<AdminUserSubscriptionResponse>>;

public sealed class UpdateUserSubscriptionStatusCommandHandler
    : IRequestHandler<UpdateUserSubscriptionStatusCommand, Result<AdminUserSubscriptionResponse>>
{
    private const string RelationTypeSubscription = "Subscription";
    private const int NotificationReasonPreviewMaxLength = 500;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IEmailRepository _emailRepository;
    private readonly IBus _bus;
    private readonly ILogger<UpdateUserSubscriptionStatusCommandHandler> _logger;

    public UpdateUserSubscriptionStatusCommandHandler(
        IUnitOfWork unitOfWork,
        IEmailRepository emailRepository,
        IBus bus,
        ILogger<UpdateUserSubscriptionStatusCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userRepository = unitOfWork.Repository<User>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _emailRepository = emailRepository;
        _bus = bus;
        _logger = logger;
    }

    public async Task<Result<AdminUserSubscriptionResponse>> Handle(
        UpdateUserSubscriptionStatusCommand request,
        CancellationToken cancellationToken)
    {
        var userSubscription = await _userSubscriptionRepository.GetAll()
            .FirstOrDefaultAsync(
                item => item.Id == request.UserSubscriptionId && !item.IsDeleted,
                cancellationToken);

        if (userSubscription == null)
        {
            return Result.Failure<AdminUserSubscriptionResponse>(
                new Error("UserSubscription.NotFound", "User subscription not found."));
        }

        var previousStatus = userSubscription.Status;
        var updatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        var reasonHtml = request.Reason.Trim();
        var reasonText = ToPlainText(reasonHtml);

        userSubscription.Status = NormalizeStatus(request.Status);
        userSubscription.UpdatedAt = updatedAt;

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userSubscription.UserId, cancellationToken);

        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userSubscription.SubscriptionId, cancellationToken);

        var payment = await _transactionRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.UserId == userSubscription.UserId &&
                item.RelationId == userSubscription.SubscriptionId &&
                item.RelationType != null &&
                item.RelationType.ToLower() == RelationTypeSubscription.ToLower())
            .OrderByDescending(item => item.CreatedAt ?? item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var response = GetAdminUserSubscriptionsQueryHandler.ToResponse(
            userSubscription,
            user,
            subscription,
            payment);

        // Persist before publishing so the notification references committed state.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await PublishStatusChangedNotificationAsync(
            userSubscription,
            subscription,
            previousStatus,
            response.DisplayStatus,
            reasonHtml,
            reasonText,
            updatedAt,
            cancellationToken);
        await SendStatusChangedEmailAsync(
            user,
            subscription,
            response.DisplayStatus,
            reasonHtml,
            reasonText,
            cancellationToken);

        return Result.Success(response);
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = status.Trim();

        return normalized.ToLowerInvariant() switch
        {
            "active" => "Active",
            "scheduled" => "Scheduled",
            "expired" => "Expired",
            "superseded" => "Superseded",
            "non_renewable" or "non-renewable" or "nonrenewable" => "non_renewable",
            _ => normalized
        };
    }

    private async Task PublishStatusChangedNotificationAsync(
        UserSubscription userSubscription,
        Subscription? subscription,
        string? previousStatus,
        string displayStatus,
        string reasonHtml,
        string reasonText,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var subscriptionName = string.IsNullOrWhiteSpace(subscription?.Name)
            ? "your subscription"
            : subscription.Name.Trim();

        var reasonPreview = Truncate(reasonText, NotificationReasonPreviewMaxLength);
        var message = $"Your {subscriptionName} subscription status was updated to {displayStatus}. Reason: {reasonPreview}";

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                userSubscription.UserId,
                NotificationTypes.UserSubscriptionStatusChanged,
                "Subscription status updated",
                message,
                new
                {
                    userSubscriptionId = userSubscription.Id,
                    subscriptionId = userSubscription.SubscriptionId,
                    subscriptionName,
                    previousStatus,
                    status = userSubscription.Status,
                    displayStatus,
                    reason = reasonHtml,
                    reasonHtml,
                    reasonText,
                    activeDate = userSubscription.ActiveDate,
                    endDate = userSubscription.EndDate
                },
                createdAt: createdAt,
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }

    private async Task SendStatusChangedEmailAsync(
        User? user,
        Subscription? subscription,
        string displayStatus,
        string reasonHtml,
        string reasonText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user?.Email))
        {
            return;
        }

        var subscriptionName = string.IsNullOrWhiteSpace(subscription?.Name)
            ? "your subscription"
            : subscription.Name.Trim();

        var subject = "Subscription status updated";
        var encodedSubscriptionName = WebUtility.HtmlEncode(subscriptionName);
        var encodedDisplayStatus = WebUtility.HtmlEncode(displayStatus);
        var htmlBody = $"""
            <p>Your <strong>{encodedSubscriptionName}</strong> subscription status was updated to <strong>{encodedDisplayStatus}</strong>.</p>
            <p><strong>Reason:</strong></p>
            <div>{reasonHtml}</div>
            """;
        var textBody = $"Your {subscriptionName} subscription status was updated to {displayStatus}. Reason: {reasonText}";

        try
        {
            await _emailRepository.SendEmailAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to send subscription status email to user {UserId}.",
                user.Id);
        }
    }

    private static string ToPlainText(string html)
    {
        var withSpaces = Regex.Replace(
            html,
            @"<(br|/p|/div|/li|/h[1-6])\b[^>]*>",
            " ",
            RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withSpaces, "<[^>]+>", string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }
}
