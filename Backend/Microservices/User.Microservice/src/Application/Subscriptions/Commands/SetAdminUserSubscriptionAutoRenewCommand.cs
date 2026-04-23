using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Helpers;
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

public sealed record SetAdminUserSubscriptionAutoRenewCommand(
    Guid UserSubscriptionId,
    bool Enabled,
    string Reason) : IRequest<Result<AdminUserSubscriptionResponse>>;

public sealed class SetAdminUserSubscriptionAutoRenewCommandHandler
    : IRequestHandler<SetAdminUserSubscriptionAutoRenewCommand, Result<AdminUserSubscriptionResponse>>
{
    private const string RelationTypeSubscription = "Subscription";
    private const int NotificationReasonPreviewMaxLength = 500;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IEmailRepository _emailRepository;
    private readonly IBus _bus;
    private readonly ILogger<SetAdminUserSubscriptionAutoRenewCommandHandler> _logger;

    public SetAdminUserSubscriptionAutoRenewCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        IEmailRepository emailRepository,
        IBus bus,
        ILogger<SetAdminUserSubscriptionAutoRenewCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userRepository = unitOfWork.Repository<User>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _stripePaymentService = stripePaymentService;
        _emailRepository = emailRepository;
        _bus = bus;
        _logger = logger;
    }

    public async Task<Result<AdminUserSubscriptionResponse>> Handle(
        SetAdminUserSubscriptionAutoRenewCommand request,
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

        if (!SubscriptionHelpers.CanSetAutoRenew(userSubscription.Status))
        {
            return Result.Failure<AdminUserSubscriptionResponse>(
                new Error(
                    "UserSubscription.AutoRenewNotAllowed",
                    "Auto-renew can only be changed for an active current user subscription."));
        }

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userSubscription.UserId, cancellationToken);

        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userSubscription.SubscriptionId, cancellationToken);

        if (request.Enabled)
        {
            var eligibilityError = ValidateCanEnableAutoRenew(userSubscription, subscription);
            if (eligibilityError != null)
            {
                return Result.Failure<AdminUserSubscriptionResponse>(eligibilityError);
            }
        }

        var previousStatus = userSubscription.Status;
        var previousAutoRenewStatus = SubscriptionHelpers.ResolveAutoRenewStatus(
            userSubscription,
            subscription,
            isScheduled: false);
        var updatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        var reasonHtml = request.Reason.Trim();
        var reasonText = ToPlainText(reasonHtml);

        if (!string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId))
        {
            var metadata = BuildStripeMetadata(
                userSubscription.UserId,
                userSubscription.SubscriptionId,
                request.Enabled);

            try
            {
                var stripeResult = await _stripePaymentService.SetSubscriptionAutoRenewAsync(
                    userSubscription.StripeSubscriptionId,
                    request.Enabled ? null : userSubscription.StripeScheduleId,
                    request.Enabled,
                    metadata,
                    cancellationToken);

                userSubscription.StripeScheduleId = string.IsNullOrWhiteSpace(stripeResult.StripeScheduleId)
                    ? null
                    : stripeResult.StripeScheduleId;

                if (stripeResult.CurrentPeriodEnd.HasValue)
                {
                    userSubscription.EndDate = stripeResult.CurrentPeriodEnd;
                }
            }
            catch (Exception ex)
            {
                var code = request.Enabled
                    ? "Stripe.EnableAutoRenewFailed"
                    : "Stripe.DisableAutoRenewFailed";

                return Result.Failure<AdminUserSubscriptionResponse>(
                    new Error(code, ex.Message));
            }
        }

        userSubscription.Status = request.Enabled ? "Active" : "non_renewable";
        userSubscription.UpdatedAt = updatedAt;
        _userSubscriptionRepository.Update(userSubscription);

        if (!request.Enabled)
        {
            await SupersedeScheduledChangesAsync(userSubscription.UserId, userSubscription.Id, updatedAt, cancellationToken);
        }

        var payment = await GetLatestPaymentAsync(userSubscription, cancellationToken);
        var response = GetAdminUserSubscriptionsQueryHandler.ToResponse(
            userSubscription,
            user,
            subscription,
            payment);

        // Persist before publishing so notification payloads point to committed state.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await PublishAutoRenewChangedNotificationAsync(
            userSubscription,
            subscription,
            previousStatus,
            previousAutoRenewStatus,
            response.AutoRenewStatus,
            request.Enabled,
            reasonHtml,
            reasonText,
            updatedAt,
            cancellationToken);
        await SendAutoRenewChangedEmailAsync(
            user,
            subscription,
            request.Enabled,
            reasonHtml,
            reasonText,
            cancellationToken);

        return Result.Success(response);
    }

    private async Task SupersedeScheduledChangesAsync(
        Guid userId,
        Guid currentUserSubscriptionId,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        var scheduledSubscriptions = await _userSubscriptionRepository.GetAll()
            .Where(item =>
                item.UserId == userId &&
                item.Id != currentUserSubscriptionId &&
                !item.IsDeleted &&
                item.Status != null &&
                item.Status.ToLower() == "scheduled")
            .ToListAsync(cancellationToken);

        foreach (var scheduledSubscription in scheduledSubscriptions)
        {
            scheduledSubscription.Status = "Superseded";
            scheduledSubscription.EndDate = updatedAt;
            scheduledSubscription.StripeScheduleId = null;
            scheduledSubscription.UpdatedAt = updatedAt;
            _userSubscriptionRepository.Update(scheduledSubscription);
        }
    }

    private async Task<Transaction?> GetLatestPaymentAsync(
        UserSubscription userSubscription,
        CancellationToken cancellationToken)
    {
        return await _transactionRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.UserId == userSubscription.UserId &&
                item.RelationId == userSubscription.SubscriptionId &&
                item.RelationType != null &&
                item.RelationType.ToLower() == RelationTypeSubscription.ToLower())
            .OrderByDescending(item => item.CreatedAt ?? item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Error? ValidateCanEnableAutoRenew(
        UserSubscription userSubscription,
        Subscription? subscription)
    {
        if (subscription == null || subscription.IsDeleted || !subscription.IsActive)
        {
            return new Error(
                "Subscription.PlanNotRenewable",
                "This subscription plan cannot be renewed.");
        }

        if (string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId))
        {
            return new Error(
                "Subscription.MissingStripeState",
                "Auto-renew can only be enabled for a Stripe recurring subscription.");
        }

        return null;
    }

    private async Task PublishAutoRenewChangedNotificationAsync(
        UserSubscription userSubscription,
        Subscription? subscription,
        string? previousStatus,
        string previousAutoRenewStatus,
        string autoRenewStatus,
        bool enabled,
        string reasonHtml,
        string reasonText,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var subscriptionName = string.IsNullOrWhiteSpace(subscription?.Name)
            ? "your subscription"
            : subscription.Name.Trim();
        var stateText = enabled ? "enabled" : "disabled";
        var reasonPreview = Truncate(reasonText, NotificationReasonPreviewMaxLength);
        var message = $"Your {subscriptionName} subscription auto-renew was {stateText}. Reason: {reasonPreview}";

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                userSubscription.UserId,
                NotificationTypes.UserSubscriptionAutoRenewChanged,
                "Subscription auto-renew updated",
                message,
                new
                {
                    userSubscriptionId = userSubscription.Id,
                    subscriptionId = userSubscription.SubscriptionId,
                    subscriptionName,
                    previousStatus,
                    status = userSubscription.Status,
                    previousAutoRenewStatus,
                    autoRenewStatus,
                    isAutoRenewEnabled = enabled,
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

    private async Task SendAutoRenewChangedEmailAsync(
        User? user,
        Subscription? subscription,
        bool enabled,
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
        var stateText = enabled ? "enabled" : "disabled";
        var subject = "Subscription auto-renew updated";
        var encodedSubscriptionName = WebUtility.HtmlEncode(subscriptionName);
        var encodedStateText = WebUtility.HtmlEncode(stateText);
        var htmlBody = $"""
            <p>Your <strong>{encodedSubscriptionName}</strong> subscription auto-renew was <strong>{encodedStateText}</strong>.</p>
            <p><strong>Reason:</strong></p>
            <div>{reasonHtml}</div>
            """;
        var textBody = $"Your {subscriptionName} subscription auto-renew was {stateText}. Reason: {reasonText}";

        try
        {
            await _emailRepository.SendEmailAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to send subscription auto-renew email to user {UserId}.",
                user.Id);
        }
    }

    private static Dictionary<string, string> BuildStripeMetadata(
        Guid userId,
        Guid subscriptionId,
        bool enabled)
    {
        return new Dictionary<string, string>
        {
            ["subscription_id"] = subscriptionId.ToString(),
            ["user_id"] = userId.ToString(),
            ["renew"] = enabled ? "true" : "false"
        };
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
