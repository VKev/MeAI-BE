using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Models;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record PurchaseSubscriptionCommand(
    Guid SubscriptionId,
    Guid UserId,
    string? PaymentMethodId,
    bool Renew) : IRequest<Result<PurchaseSubscriptionResponse>>;

public sealed class PurchaseSubscriptionCommandHandler
    : IRequestHandler<PurchaseSubscriptionCommand, Result<PurchaseSubscriptionResponse>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IBus _bus;

    public PurchaseSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        IBus bus)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userRepository = unitOfWork.Repository<User>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripePaymentService = stripePaymentService;
        _bus = bus;
    }

    public async Task<Result<PurchaseSubscriptionResponse>> Handle(
        PurchaseSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == request.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        if (subscription == null)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        if (!subscription.Cost.HasValue || subscription.Cost.Value <= 0)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.InvalidCost", "Subscription cost is not valid."));
        }

        var paymentMethodId = string.IsNullOrWhiteSpace(request.PaymentMethodId)
            ? null
            : request.PaymentMethodId.Trim();
        if (subscription.DurationMonths <= 0)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.InvalidDuration", "Subscription duration is not valid."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var hasActiveSubscription = await _userSubscriptionRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.UserId == request.UserId &&
                    item.SubscriptionId == request.SubscriptionId &&
                    !item.IsDeleted &&
                    (!item.EndDate.HasValue || item.EndDate.Value >= now),
                cancellationToken);

        if (hasActiveSubscription)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.AlreadyActive", "This plan is already active."));
        }

        var cost = Convert.ToDecimal(subscription.Cost.Value);
        StripePaymentIntentResult? paymentIntent = null;
        StripeSubscriptionResult? stripeSubscription = null;
        var transactionId = Guid.CreateVersion7();
        var metadata = new Dictionary<string, string>
        {
            ["subscription_id"] = subscription.Id.ToString(),
            ["user_id"] = request.UserId.ToString(),
            ["transaction_id"] = transactionId.ToString(),
            ["renew"] = request.Renew.ToString().ToLowerInvariant(),
            ["duration_months"] = subscription.DurationMonths.ToString()
        };
        try
        {
            if (request.Renew)
            {
                var user = await _userRepository.GetAll()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, cancellationToken);

                if (user == null)
                {
                    return Result.Failure<PurchaseSubscriptionResponse>(
                        new Error("User.NotFound", "User not found."));
                }

                stripeSubscription = await _stripePaymentService.CreateSubscriptionAsync(
                    cost,
                    subscription.DurationMonths,
                    paymentMethodId,
                    user.Email,
                    user.FullName ?? user.Username,
                    subscription.Name,
                    metadata,
                    cancellationToken);
            }
            else
            {
                paymentIntent = await _stripePaymentService.CreatePaymentIntentAsync(
                    cost,
                    paymentMethodId,
                    metadata,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Stripe.PaymentFailed", ex.Message));
        }

        var status = request.Renew
            ? stripeSubscription!.Status
            : paymentIntent!.Status;
        var paymentIntentId = request.Renew
            ? stripeSubscription!.PaymentIntentId
            : paymentIntent!.PaymentIntentId;
        var clientSecret = request.Renew
            ? stripeSubscription!.ClientSecret
            : paymentIntent!.ClientSecret;
        var currency = request.Renew
            ? stripeSubscription!.Currency
            : paymentIntent!.Currency;
        var amount = request.Renew
            ? stripeSubscription!.Amount
            : paymentIntent!.Amount;

        var transaction = new Transaction
        {
            Id = transactionId,
            UserId = request.UserId,
            RelationId = subscription.Id,
            RelationType = "Subscription",
            Cost = cost,
            TransactionType = request.Renew ? "SubscriptionRecurring" : "SubscriptionPurchase",
            PaymentMethod = "Stripe",
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        Guid? userSubscriptionId = null;
        DateTime? userSubscriptionEndDate = null;
        var subscriptionActivated = false;
        var isPaymentSucceeded = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
        var isSubscriptionActive = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "trialing", StringComparison.OrdinalIgnoreCase);
        if ((!request.Renew && isPaymentSucceeded) || (request.Renew && isSubscriptionActive))
        {
            var userSubscription = new UserSubscription
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                SubscriptionId = subscription.Id,
                ActiveDate = now,
                EndDate = now.AddMonths(subscription.DurationMonths),
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _userSubscriptionRepository.AddAsync(userSubscription, cancellationToken);
            userSubscriptionId = userSubscription.Id;
            userSubscriptionEndDate = userSubscription.EndDate;
            subscriptionActivated = true;
        }

        var response = new PurchaseSubscriptionResponse(
            subscription.Id,
            subscription.Cost.Value,
            currency,
            amount,
            paymentIntentId,
            clientSecret,
            status,
            stripeSubscription?.SubscriptionId,
            request.Renew,
            transaction.Id,
            subscriptionActivated,
            userSubscriptionId);

        if (subscriptionActivated)
        {
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
                        userSubscriptionId,
                        userSubscriptionEndDate
                    },
                    request.UserId,
                    now),
                cancellationToken);
        }

        return Result.Success(response);
    }
}
