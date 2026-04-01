using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
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
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IUserSubscriptionStateService _userSubscriptionStateService;
    private readonly ISender _sender;

    public PurchaseSubscriptionCommandHandler(
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        IUserSubscriptionStateService userSubscriptionStateService,
        ISender sender)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _userRepository = unitOfWork.Repository<User>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripePaymentService = stripePaymentService;
        _userSubscriptionStateService = userSubscriptionStateService;
        _sender = sender;
    }

    public async Task<Result<PurchaseSubscriptionResponse>> Handle(
        PurchaseSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetAll()
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

        if (subscription.DurationMonths <= 0)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.InvalidDuration", "Subscription duration is not valid."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var state = await _userSubscriptionStateService.GetStateAsync(request.UserId, cancellationToken);

        if (state.Current?.SubscriptionId == request.SubscriptionId)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.AlreadyActive", "This plan is already active."));
        }

        if (state.Scheduled?.SubscriptionId == request.SubscriptionId)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.AlreadyScheduled", "This plan is already scheduled as your next plan."));
        }

        if (state.Scheduled != null)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.ChangeAlreadyScheduled", "A future plan change is already scheduled."));
        }

        var currentPlan = state.Current == null
            ? null
            : await _subscriptionRepository.GetAll()
                .FirstOrDefaultAsync(
                    item => item.Id == state.Current.SubscriptionId && !item.IsDeleted,
                    cancellationToken);

        var changeType = ResolveChangeType(state.Current, currentPlan, Convert.ToDecimal(subscription.Cost.Value));
        var targetCatalog = await EnsurePlanCatalogAsync(subscription, cancellationToken);

        if (string.Equals(changeType, SubscriptionChangeTypes.ScheduledChange, StringComparison.OrdinalIgnoreCase))
        {
            return await ScheduleRecurringChangeAsync(
                request,
                subscription,
                currentPlan,
                state,
                targetCatalog,
                now,
                cancellationToken);
        }

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.UserId && !item.IsDeleted, cancellationToken);

        if (user == null)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("User.NotFound", "User not found."));
        }

        var transactionId = Guid.CreateVersion7();
        var metadata = BuildStripeMetadata(
            request.UserId,
            subscription.Id,
            transactionId,
            renew: true);
        var transactionType = SubscriptionChangeTypes.ToTransactionType(changeType);

        StripeRecurringSubscriptionResult stripeResult;
        try
        {
            if (string.Equals(changeType, SubscriptionChangeTypes.Upgrade, StringComparison.OrdinalIgnoreCase))
            {
                if (state.Current == null || string.IsNullOrWhiteSpace(state.Current.StripeSubscriptionId))
                {
                    return Result.Failure<PurchaseSubscriptionResponse>(
                        new Error("Subscription.MissingStripeState", "Current subscription is missing Stripe recurring state."));
                }

                stripeResult = await _stripePaymentService.UpgradeSubscriptionAsync(
                    state.Current.StripeSubscriptionId,
                    targetCatalog.StripePriceId,
                    metadata,
                    cancellationToken);
            }
            else
            {
                stripeResult = await _stripePaymentService.CreateSubscriptionAsync(
                    targetCatalog.StripePriceId,
                    user.Email,
                    user.FullName ?? user.Username,
                    metadata,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Stripe.PaymentFailed", ex.Message));
        }

        var transaction = new Transaction
        {
            Id = transactionId,
            UserId = request.UserId,
            RelationId = subscription.Id,
            RelationType = "Subscription",
            Cost = stripeResult.AmountDue,
            TransactionType = transactionType,
            PaymentMethod = "Stripe",
            Status = stripeResult.Status,
            ProviderReferenceId = stripeResult.PaymentIntentId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        Guid? userSubscriptionId = null;
        var subscriptionActivated = false;
        var effectiveDate = stripeResult.CurrentPeriodStart;
        var requiresPayment = !string.IsNullOrWhiteSpace(stripeResult.ClientSecret) && !IsSuccessStatus(stripeResult.Status);

        if (IsSuccessStatus(stripeResult.Status))
        {
            var confirmResult = await _sender.Send(
                new ConfirmSubscriptionPaymentCommand(
                    request.UserId,
                    request.SubscriptionId,
                    transactionId,
                    stripeResult.PaymentIntentId,
                    stripeResult.StripeSubscriptionId,
                    true,
                    stripeResult.Status),
                cancellationToken);

            if (confirmResult.IsFailure)
            {
                return Result.Failure<PurchaseSubscriptionResponse>(confirmResult.Error);
            }

            userSubscriptionId = confirmResult.Value.UserSubscriptionId;
            subscriptionActivated = confirmResult.Value.SubscriptionActivated;
            effectiveDate = confirmResult.Value.EffectiveDate ?? effectiveDate;
        }

        return Result.Success(new PurchaseSubscriptionResponse(
            subscription.Id,
            subscription.Cost.Value,
            stripeResult.Currency,
            stripeResult.AmountDue,
            0m,
            stripeResult.PaymentIntentId,
            stripeResult.ClientSecret,
            stripeResult.Status,
            stripeResult.StripeSubscriptionId,
            true,
            transaction.Id,
            subscriptionActivated,
            false,
            userSubscriptionId,
            changeType,
            effectiveDate,
            requiresPayment));
    }

    private async Task<Result<PurchaseSubscriptionResponse>> ScheduleRecurringChangeAsync(
        PurchaseSubscriptionCommand request,
        Subscription targetPlan,
        Subscription? currentPlan,
        UserSubscriptionState state,
        StripeCatalogPriceResult targetCatalog,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (state.Current == null || string.IsNullOrWhiteSpace(state.Current.StripeSubscriptionId) || currentPlan == null)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Subscription.MissingStripeState", "Current subscription is missing Stripe recurring state."));
        }

        var currentCatalog = await EnsurePlanCatalogAsync(currentPlan, cancellationToken);
        var currentMetadata = BuildStripeMetadata(
            request.UserId,
            currentPlan.Id,
            null,
            renew: true);
        var nextMetadata = BuildStripeMetadata(
            request.UserId,
            targetPlan.Id,
            null,
            renew: true);

        StripeScheduledChangeResult scheduleResult;
        try
        {
            scheduleResult = await _stripePaymentService.ScheduleSubscriptionChangeAsync(
                state.Current.StripeSubscriptionId,
                currentCatalog.StripePriceId,
                targetCatalog.StripePriceId,
                currentMetadata,
                nextMetadata,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<PurchaseSubscriptionResponse>(
                new Error("Stripe.ScheduleFailed", ex.Message));
        }

        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            RelationId = targetPlan.Id,
            RelationType = "Subscription",
            Cost = 0m,
            TransactionType = SubscriptionChangeTypes.ToTransactionType(SubscriptionChangeTypes.ScheduledChange),
            PaymentMethod = "Stripe",
            Status = "scheduled",
            ProviderReferenceId = scheduleResult.StripeScheduleId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        var scheduledStart = scheduleResult.CurrentPeriodEnd ?? state.Current.EndDate ?? now;
        var scheduledSubscription = new UserSubscription
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            SubscriptionId = targetPlan.Id,
            ActiveDate = scheduledStart,
            EndDate = scheduledStart.AddMonths(targetPlan.DurationMonths),
            Status = "Scheduled",
            StripeSubscriptionId = state.Current.StripeSubscriptionId,
            StripeScheduleId = scheduleResult.StripeScheduleId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _userSubscriptionRepository.AddAsync(scheduledSubscription, cancellationToken);

        return Result.Success(new PurchaseSubscriptionResponse(
            targetPlan.Id,
            targetPlan.Cost!.Value,
            "vnd",
            0m,
            0m,
            null,
            null,
            "scheduled",
            scheduleResult.StripeSubscriptionId,
            true,
            transaction.Id,
            false,
            true,
            scheduledSubscription.Id,
            SubscriptionChangeTypes.ScheduledChange,
            scheduledSubscription.ActiveDate,
            false));
    }

    private async Task<StripeCatalogPriceResult> EnsurePlanCatalogAsync(
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var result = await _stripePaymentService.EnsureRecurringPriceAsync(
            subscription.StripeProductId,
            subscription.StripePriceId,
            Convert.ToDecimal(subscription.Cost!.Value),
            subscription.DurationMonths,
            subscription.Name,
            cancellationToken);

        if (!string.Equals(subscription.StripeProductId, result.StripeProductId, StringComparison.Ordinal) ||
            !string.Equals(subscription.StripePriceId, result.StripePriceId, StringComparison.Ordinal))
        {
            subscription.StripeProductId = result.StripeProductId;
            subscription.StripePriceId = result.StripePriceId;
            subscription.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _subscriptionRepository.Update(subscription);
        }

        return result;
    }

    private static Dictionary<string, string> BuildStripeMetadata(
        Guid userId,
        Guid subscriptionId,
        Guid? transactionId,
        bool renew)
    {
        var metadata = new Dictionary<string, string>
        {
            ["subscription_id"] = subscriptionId.ToString(),
            ["user_id"] = userId.ToString(),
            ["renew"] = renew ? "true" : "false"
        };

        if (transactionId.HasValue)
        {
            metadata["transaction_id"] = transactionId.Value.ToString();
        }

        return metadata;
    }

    private static string ResolveChangeType(
        UserSubscription? currentSubscription,
        Subscription? currentPlan,
        decimal targetCost)
    {
        if (currentSubscription == null || currentPlan?.Cost == null)
        {
            return SubscriptionChangeTypes.NewPurchase;
        }

        return targetCost > Convert.ToDecimal(currentPlan.Cost.Value)
            ? SubscriptionChangeTypes.Upgrade
            : SubscriptionChangeTypes.ScheduledChange;
    }

    private static bool IsSuccessStatus(string? value) =>
        string.Equals(value?.Trim(), "succeeded", StringComparison.OrdinalIgnoreCase);
}
