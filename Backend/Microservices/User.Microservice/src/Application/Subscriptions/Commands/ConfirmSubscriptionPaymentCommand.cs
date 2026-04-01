using Application.Abstractions.Data;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record ConfirmSubscriptionPaymentCommand(
    Guid UserId,
    Guid SubscriptionId,
    Guid? TransactionId,
    bool Renew,
    string Status) : IRequest<Result<bool>>;

public sealed class ConfirmSubscriptionPaymentCommandHandler
    : IRequestHandler<ConfirmSubscriptionPaymentCommand, Result<bool>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IBus _bus;

    public ConfirmSubscriptionPaymentCommandHandler(IUnitOfWork unitOfWork, IBus bus)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _bus = bus;
    }

    public async Task<Result<bool>> Handle(
        ConfirmSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == request.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        if (subscription == null)
        {
            return Result.Failure<bool>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var normalizedStatus = NormalizeStatus(request.Status);
        var transactionType = request.Renew ? "SubscriptionRecurring" : "SubscriptionPurchase";
        var durationMonths = subscription.DurationMonths > 0 ? subscription.DurationMonths : 1;
        var isSuccessful = IsSuccessfulStatus(normalizedStatus);

        var transactions = _transactionRepository.GetAll()
            .Where(item =>
                item.UserId == request.UserId &&
                item.RelationId == subscription.Id &&
                item.TransactionType == transactionType &&
                item.PaymentMethod == "Stripe" &&
                !item.IsDeleted);

        Transaction? transaction = null;

        if (request.TransactionId.HasValue)
        {
            transaction = await transactions
                .FirstOrDefaultAsync(item => item.Id == request.TransactionId.Value, cancellationToken);
        }

        transaction ??= await transactions
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var alreadySucceeded = transaction != null && IsSuccessfulStatus(transaction.Status);

        if (transaction == null)
        {
            transaction = new Transaction
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                RelationId = subscription.Id,
                RelationType = "Subscription",
                Cost = subscription.Cost.HasValue ? Convert.ToDecimal(subscription.Cost.Value) : null,
                TransactionType = transactionType,
                PaymentMethod = "Stripe",
                Status = normalizedStatus,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false
            };

            await _transactionRepository.AddAsync(transaction, cancellationToken);
        }
        else
        {
            if (alreadySucceeded)
            {
                return Result.Success(true);
            }

            transaction.Status = normalizedStatus;
            transaction.UpdatedAt = now;
            _transactionRepository.Update(transaction);
        }

        if (!isSuccessful)
        {
            return Result.Success(true);
        }

        var userSubscription = await _userSubscriptionRepository.GetAll()
            .Where(item =>
                item.UserId == request.UserId &&
                item.SubscriptionId == subscription.Id &&
                !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (userSubscription == null)
        {
            userSubscription = new UserSubscription
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                SubscriptionId = subscription.Id,
                ActiveDate = now,
                EndDate = now.AddMonths(durationMonths),
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false
            };

            await _userSubscriptionRepository.AddAsync(userSubscription, cancellationToken);
        }
        else
        {
            var endDate = userSubscription.EndDate;
            if (request.Renew)
            {
                var baseDate = endDate.HasValue && endDate.Value > now ? endDate.Value : now;
                endDate = baseDate.AddMonths(durationMonths);
            }
            else if (!endDate.HasValue)
            {
                endDate = now.AddMonths(durationMonths);
            }

            userSubscription.Status = "Active";
            userSubscription.ActiveDate ??= now;
            userSubscription.EndDate = endDate;
            userSubscription.UpdatedAt = now;
            _userSubscriptionRepository.Update(userSubscription);
        }

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                request.Renew ? NotificationTypes.UserSubscriptionRenewed : NotificationTypes.UserSubscriptionActivated,
                request.Renew ? "Subscription renewed" : "Subscription activated",
                request.Renew
                    ? $"Your {subscription.Name} subscription was renewed successfully."
                    : $"Your {subscription.Name} subscription is now active.",
                new
                {
                    subscriptionId = subscription.Id,
                    subscription.Name,
                    transaction.Id,
                    request.Renew,
                    userSubscriptionId = userSubscription.Id,
                    userSubscription.EndDate
                },
                request.UserId,
                now),
            cancellationToken);

        return Result.Success(true);
    }

    private static string NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "succeeded";
        }

        var normalized = value.Trim();

        if (string.Equals(normalized, "paid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "active", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "trialing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "complete", StringComparison.OrdinalIgnoreCase))
        {
            return "succeeded";
        }

        if (string.Equals(normalized, "processing", StringComparison.OrdinalIgnoreCase))
        {
            return "pending";
        }

        if (string.Equals(normalized, "canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        return normalized;
    }

    private static bool IsSuccessfulStatus(string? value) =>
        string.Equals(value?.Trim(), "succeeded", StringComparison.OrdinalIgnoreCase);
}
