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

public sealed record ConfirmSubscriptionPaymentCommand(
    Guid UserId,
    Guid SubscriptionId,
    Guid? TransactionId,
    string? ProviderReferenceId,
    string? StripeSubscriptionId,
    bool Renew,
    string Status) : IRequest<Result<ConfirmSubscriptionPaymentResponse>>;

public sealed class ConfirmSubscriptionPaymentCommandHandler
    : IRequestHandler<ConfirmSubscriptionPaymentCommand, Result<ConfirmSubscriptionPaymentResponse>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IUserSubscriptionStateService _userSubscriptionStateService;
    private readonly IStripePaymentService _stripePaymentService;

    public ConfirmSubscriptionPaymentCommandHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionStateService userSubscriptionStateService,
        IStripePaymentService stripePaymentService)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
        _userRepository = unitOfWork.Repository<User>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userSubscriptionStateService = userSubscriptionStateService;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<ConfirmSubscriptionPaymentResponse>> Handle(
        ConfirmSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetAll()
            .FirstOrDefaultAsync(
                item => item.Id == request.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        if (subscription == null)
        {
            return Result.Failure<ConfirmSubscriptionPaymentResponse>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var state = await _userSubscriptionStateService.GetStateAsync(request.UserId, cancellationToken);
        var normalizedStatus = NormalizeStatus(request.Status);
        var isSuccessful = IsSuccessfulStatus(normalizedStatus);

        var transactions = _transactionRepository.GetAll()
            .Where(item =>
                item.UserId == request.UserId &&
                item.RelationId == subscription.Id &&
                item.PaymentMethod == "Stripe" &&
                !item.IsDeleted);

        Transaction? transaction = null;

        if (request.TransactionId.HasValue)
        {
            var trackedTransaction = await _transactionRepository.GetByIdAsync(request.TransactionId.Value, cancellationToken);
            if (trackedTransaction != null &&
                trackedTransaction.UserId == request.UserId &&
                trackedTransaction.RelationId == subscription.Id &&
                string.Equals(trackedTransaction.PaymentMethod, "Stripe", StringComparison.OrdinalIgnoreCase) &&
                !trackedTransaction.IsDeleted)
            {
                transaction = trackedTransaction;
            }
        }

        if (transaction == null && !string.IsNullOrWhiteSpace(request.ProviderReferenceId))
        {
            transaction = await transactions
                .FirstOrDefaultAsync(item => item.ProviderReferenceId == request.ProviderReferenceId, cancellationToken);
        }

        transaction ??= await transactions
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var transactionType = transaction?.TransactionType ?? (request.Renew ? "SubscriptionRecurring" : "SubscriptionPurchase");
        var changeType = SubscriptionChangeTypes.FromTransactionType(transactionType);
        var isRecurringRenewal = string.Equals(transactionType, "SubscriptionRecurring", StringComparison.OrdinalIgnoreCase);

        if (transaction == null)
        {
            transaction = new Transaction
            {
                Id = request.TransactionId ?? Guid.CreateVersion7(),
                UserId = request.UserId,
                RelationId = subscription.Id,
                RelationType = "Subscription",
                Cost = subscription.Cost.HasValue ? Convert.ToDecimal(subscription.Cost.Value) : null,
                TransactionType = transactionType,
                PaymentMethod = "Stripe",
                Status = normalizedStatus,
                ProviderReferenceId = request.ProviderReferenceId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false
            };

            await _transactionRepository.AddAsync(transaction, cancellationToken);
        }
        else
        {
            transaction.Status = normalizedStatus;
            transaction.ProviderReferenceId ??= request.ProviderReferenceId;
            transaction.UpdatedAt = now;
        }

        if (!isSuccessful)
        {
            return Result.Success(new ConfirmSubscriptionPaymentResponse(
                changeType,
                false,
                false,
                null,
                null));
        }

        StripeSubscriptionSnapshotResult? stripeSnapshot = null;
        if (!string.IsNullOrWhiteSpace(request.StripeSubscriptionId))
        {
            stripeSnapshot = await _stripePaymentService.GetSubscriptionSnapshotAsync(
                request.StripeSubscriptionId,
                cancellationToken);
        }

        if (isRecurringRenewal)
        {
            if (stripeSnapshot == null)
            {
                return Result.Failure<ConfirmSubscriptionPaymentResponse>(
                    new Error("Stripe.SubscriptionMissing", "Stripe subscription details are required to process recurring renewals."));
            }

            return Result.Success(await ApplyRecurringRenewalAsync(
                request.UserId,
                subscription,
                now,
                state,
                stripeSnapshot,
                cancellationToken));
        }

        if (string.Equals(changeType, SubscriptionChangeTypes.ScheduledChange, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(await ApplyScheduledChangeAsync(
                request.UserId,
                subscription,
                now,
                state,
                cancellationToken));
        }

        if (stripeSnapshot == null)
        {
            return Result.Failure<ConfirmSubscriptionPaymentResponse>(
                new Error("Stripe.SubscriptionMissing", "Stripe subscription details are required to activate this plan."));
        }

        var metadata = BuildStripeMetadata(request.UserId, subscription.Id, renew: true);
        await _stripePaymentService.UpdateSubscriptionMetadataAsync(
            stripeSnapshot.StripeSubscriptionId,
            metadata,
            cancellationToken);

        if (string.Equals(changeType, SubscriptionChangeTypes.Upgrade, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(await ApplyUpgradeAsync(
                request.UserId,
                subscription,
                now,
                state,
                stripeSnapshot,
                cancellationToken));
        }

        return Result.Success(await ApplyNewPurchaseAsync(
            request.UserId,
            subscription,
            now,
            state,
            stripeSnapshot,
            cancellationToken));
    }

    private async Task<ConfirmSubscriptionPaymentResponse> ApplyNewPurchaseAsync(
        Guid userId,
        Subscription subscription,
        DateTime now,
        UserSubscriptionState state,
        StripeSubscriptionSnapshotResult stripeSnapshot,
        CancellationToken cancellationToken)
    {
        var currentPeriodStart = stripeSnapshot.CurrentPeriodStart ?? state.Current?.ActiveDate ?? now;
        var currentPeriodEnd = stripeSnapshot.CurrentPeriodEnd ?? state.Current?.EndDate ?? now.AddMonths(Math.Max(subscription.DurationMonths, 1));

        if (state.Current?.SubscriptionId == subscription.Id &&
            MatchesStripeSubscription(state.Current, stripeSnapshot))
        {
            var shouldAwardCoins = !MatchesSubscriptionPeriod(state.Current, currentPeriodEnd);
            state.Current.Status = "Active";
            state.Current.ActiveDate = currentPeriodStart;
            state.Current.EndDate = currentPeriodEnd;
            state.Current.StripeSubscriptionId = stripeSnapshot.StripeSubscriptionId;
            state.Current.StripeScheduleId = stripeSnapshot.StripeScheduleId;
            state.Current.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Current);

            if (shouldAwardCoins)
            {
                await AwardSubscriptionCoinsAsync(userId, subscription, now, cancellationToken);
            }

            return new ConfirmSubscriptionPaymentResponse(
                SubscriptionChangeTypes.NewPurchase,
                true,
                false,
                state.Current.Id,
                state.Current.ActiveDate);
        }

        if (state.Current != null)
        {
            state.Current.Status = "Superseded";
            state.Current.EndDate = now;
            state.Current.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Current);
        }

        var userSubscription = await CreateUserSubscriptionAsync(
            userId,
            subscription.Id,
            currentPeriodStart,
            currentPeriodEnd,
            "Active",
            stripeSnapshot.StripeSubscriptionId,
            stripeSnapshot.StripeScheduleId,
            cancellationToken);
        await AwardSubscriptionCoinsAsync(userId, subscription, now, cancellationToken);

        return new ConfirmSubscriptionPaymentResponse(
            SubscriptionChangeTypes.NewPurchase,
            true,
            false,
            userSubscription.Id,
            userSubscription.ActiveDate);
    }

    private async Task<ConfirmSubscriptionPaymentResponse> ApplyUpgradeAsync(
        Guid userId,
        Subscription subscription,
        DateTime now,
        UserSubscriptionState state,
        StripeSubscriptionSnapshotResult stripeSnapshot,
        CancellationToken cancellationToken)
    {
        var upgradeCoinAdjustment = await ResolveUpgradeCoinAdjustmentAsync(state, subscription, cancellationToken);
        var currentPeriodEnd = stripeSnapshot.CurrentPeriodEnd ?? state.Current?.EndDate ?? now.AddMonths(Math.Max(subscription.DurationMonths, 1));

        if (state.Current?.SubscriptionId == subscription.Id &&
            MatchesStripeSubscription(state.Current, stripeSnapshot))
        {
            var shouldAwardCoins = !MatchesSubscriptionPeriod(state.Current, currentPeriodEnd);
            state.Current.Status = "Active";
            state.Current.ActiveDate = now;
            state.Current.EndDate = currentPeriodEnd;
            state.Current.StripeSubscriptionId = stripeSnapshot.StripeSubscriptionId;
            state.Current.StripeScheduleId = stripeSnapshot.StripeScheduleId;
            state.Current.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Current);

            if (shouldAwardCoins)
            {
                await AwardCoinsAsync(userId, upgradeCoinAdjustment, now, cancellationToken);
            }

            return new ConfirmSubscriptionPaymentResponse(
                SubscriptionChangeTypes.Upgrade,
                true,
                false,
                state.Current.Id,
                state.Current.ActiveDate);
        }

        if (state.Current != null)
        {
            state.Current.Status = "Superseded";
            state.Current.EndDate = now;
            state.Current.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Current);
        }

        var userSubscription = await CreateUserSubscriptionAsync(
            userId,
            subscription.Id,
            now,
            currentPeriodEnd,
            "Active",
            stripeSnapshot.StripeSubscriptionId,
            stripeSnapshot.StripeScheduleId,
            cancellationToken);
        await AwardCoinsAsync(userId, upgradeCoinAdjustment, now, cancellationToken);

        return new ConfirmSubscriptionPaymentResponse(
            SubscriptionChangeTypes.Upgrade,
            true,
            false,
            userSubscription.Id,
            userSubscription.ActiveDate);
    }

    private async Task<ConfirmSubscriptionPaymentResponse> ApplyRecurringRenewalAsync(
        Guid userId,
        Subscription subscription,
        DateTime now,
        UserSubscriptionState state,
        StripeSubscriptionSnapshotResult stripeSnapshot,
        CancellationToken cancellationToken)
    {
        var renewalPeriodStart = stripeSnapshot.CurrentPeriodStart ?? now;
        var renewalPeriodEnd = stripeSnapshot.CurrentPeriodEnd ?? now.AddMonths(Math.Max(subscription.DurationMonths, 1));

        if (state.Scheduled?.SubscriptionId == subscription.Id &&
            MatchesStripeSubscription(state.Scheduled, stripeSnapshot))
        {
            var shouldAwardCoins = !MatchesSubscriptionPeriod(state.Scheduled, renewalPeriodEnd);
            if (state.Current != null && state.Current.Id != state.Scheduled.Id)
            {
                state.Current.Status = "Superseded";
                state.Current.EndDate = renewalPeriodStart;
                state.Current.UpdatedAt = now;
                _userSubscriptionRepository.Update(state.Current);
            }

            state.Scheduled.Status = "Active";
            state.Scheduled.ActiveDate = renewalPeriodStart;
            state.Scheduled.EndDate = renewalPeriodEnd;
            state.Scheduled.StripeSubscriptionId = stripeSnapshot.StripeSubscriptionId;
            state.Scheduled.StripeScheduleId = null;
            state.Scheduled.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Scheduled);

            if (shouldAwardCoins)
            {
                await AwardSubscriptionCoinsAsync(userId, subscription, now, cancellationToken);
            }

            return new ConfirmSubscriptionPaymentResponse(
                SubscriptionChangeTypes.ScheduledChange,
                true,
                false,
                state.Scheduled.Id,
                state.Scheduled.ActiveDate);
        }

        if (state.Current?.SubscriptionId == subscription.Id &&
            MatchesStripeSubscription(state.Current, stripeSnapshot))
        {
            var shouldAwardCoins = !MatchesSubscriptionPeriod(state.Current, renewalPeriodEnd);
            state.Current.Status = "Active";
            state.Current.ActiveDate = renewalPeriodStart;
            state.Current.EndDate = renewalPeriodEnd;
            state.Current.StripeSubscriptionId = stripeSnapshot.StripeSubscriptionId;
            state.Current.StripeScheduleId = stripeSnapshot.StripeScheduleId;
            state.Current.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Current);

            if (shouldAwardCoins)
            {
                await AwardSubscriptionCoinsAsync(userId, subscription, now, cancellationToken);
            }

            return new ConfirmSubscriptionPaymentResponse(
                SubscriptionChangeTypes.NewPurchase,
                true,
                false,
                state.Current.Id,
                state.Current.ActiveDate);
        }

        if (state.Current != null)
        {
            state.Current.Status = "Superseded";
            state.Current.EndDate = renewalPeriodStart;
            state.Current.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Current);
        }

        var userSubscription = await CreateUserSubscriptionAsync(
            userId,
            subscription.Id,
            renewalPeriodStart,
            renewalPeriodEnd,
            "Active",
            stripeSnapshot.StripeSubscriptionId,
            stripeSnapshot.StripeScheduleId,
            cancellationToken);
        await AwardSubscriptionCoinsAsync(userId, subscription, now, cancellationToken);

        return new ConfirmSubscriptionPaymentResponse(
            SubscriptionChangeTypes.NewPurchase,
            true,
            false,
            userSubscription.Id,
            userSubscription.ActiveDate);
    }

    private async Task<ConfirmSubscriptionPaymentResponse> ApplyScheduledChangeAsync(
        Guid userId,
        Subscription subscription,
        DateTime now,
        UserSubscriptionState state,
        CancellationToken cancellationToken)
    {
        if (state.Scheduled?.SubscriptionId == subscription.Id)
        {
            return new ConfirmSubscriptionPaymentResponse(
                SubscriptionChangeTypes.ScheduledChange,
                false,
                true,
                state.Scheduled.Id,
                state.Scheduled.ActiveDate);
        }

        if (state.Current == null)
        {
            var userSubscription = await CreateUserSubscriptionAsync(
                userId,
                subscription.Id,
                now,
                now.AddMonths(Math.Max(subscription.DurationMonths, 1)),
                "Active",
                null,
                null,
                cancellationToken);
            await AwardSubscriptionCoinsAsync(userId, subscription, now, cancellationToken);

            return new ConfirmSubscriptionPaymentResponse(
                SubscriptionChangeTypes.NewPurchase,
                true,
                false,
                userSubscription.Id,
                userSubscription.ActiveDate);
        }

        var scheduledStart = state.Current.EndDate.HasValue && state.Current.EndDate.Value > now
            ? state.Current.EndDate.Value
            : now;
        var scheduledSubscription = await CreateUserSubscriptionAsync(
            userId,
            subscription.Id,
            scheduledStart,
            scheduledStart.AddMonths(Math.Max(subscription.DurationMonths, 1)),
            "Scheduled",
            state.Current.StripeSubscriptionId,
            state.Current.StripeScheduleId,
            cancellationToken);

        return new ConfirmSubscriptionPaymentResponse(
            SubscriptionChangeTypes.ScheduledChange,
            false,
            true,
            scheduledSubscription.Id,
            scheduledSubscription.ActiveDate);
    }

    private async Task<UserSubscription> CreateUserSubscriptionAsync(
        Guid userId,
        Guid subscriptionId,
        DateTime activeDate,
        DateTime endDate,
        string status,
        string? stripeSubscriptionId,
        string? stripeScheduleId,
        CancellationToken cancellationToken)
    {
        var userSubscription = new UserSubscription
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SubscriptionId = subscriptionId,
            ActiveDate = activeDate,
            EndDate = endDate,
            Status = status,
            StripeSubscriptionId = stripeSubscriptionId,
            StripeScheduleId = stripeScheduleId,
            CreatedAt = activeDate,
            UpdatedAt = activeDate,
            IsDeleted = false
        };

        await _userSubscriptionRepository.AddAsync(userSubscription, cancellationToken);
        return userSubscription;
    }

    private async Task AwardSubscriptionCoinsAsync(
        Guid userId,
        Subscription subscription,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await AwardCoinsAsync(userId, subscription.MeAiCoin ?? 0m, now, cancellationToken);
    }

    private async Task AwardCoinsAsync(
        Guid userId,
        decimal coinAmount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (coinAmount <= 0m)
        {
            return;
        }

        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == userId && !item.IsDeleted, cancellationToken);

        if (user == null)
        {
            return;
        }

        user.MeAiCoin = (user.MeAiCoin ?? 0m) + coinAmount;
        user.UpdatedAt = now;
        _userRepository.Update(user);
    }

    private async Task<decimal> ResolveUpgradeCoinAdjustmentAsync(
        UserSubscriptionState state,
        Subscription targetSubscription,
        CancellationToken cancellationToken)
    {
        var targetCoinAllowance = targetSubscription.MeAiCoin ?? 0m;
        if (targetCoinAllowance <= 0m)
        {
            return 0m;
        }

        if (state.Current == null || state.Current.SubscriptionId == targetSubscription.Id)
        {
            return 0m;
        }

        var currentPlan = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == state.Current.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        var currentCoinAllowance = currentPlan?.MeAiCoin ?? 0m;
        return Math.Max(0m, targetCoinAllowance - currentCoinAllowance);
    }

    private static ConfirmSubscriptionPaymentResponse BuildExistingResponse(
        string changeType,
        Guid subscriptionId,
        UserSubscriptionState state)
    {
        if (string.Equals(changeType, SubscriptionChangeTypes.ScheduledChange, StringComparison.OrdinalIgnoreCase))
        {
            if (state.Scheduled?.SubscriptionId == subscriptionId)
            {
                return new ConfirmSubscriptionPaymentResponse(
                    changeType,
                    false,
                    true,
                    state.Scheduled.Id,
                    state.Scheduled.ActiveDate);
            }

            return new ConfirmSubscriptionPaymentResponse(changeType, false, false, null, null);
        }

        if (state.Current?.SubscriptionId == subscriptionId)
        {
            return new ConfirmSubscriptionPaymentResponse(
                changeType,
                true,
                false,
                state.Current.Id,
                state.Current.ActiveDate);
        }

        return new ConfirmSubscriptionPaymentResponse(changeType, false, false, null, null);
    }

    private static Dictionary<string, string> BuildStripeMetadata(Guid userId, Guid subscriptionId, bool renew)
    {
        return new Dictionary<string, string>
        {
            ["subscription_id"] = subscriptionId.ToString(),
            ["user_id"] = userId.ToString(),
            ["renew"] = renew ? "true" : "false"
        };
    }

    private static bool MatchesStripeSubscription(
        UserSubscription userSubscription,
        StripeSubscriptionSnapshotResult stripeSnapshot)
    {
        return string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId) ||
               string.Equals(
                   userSubscription.StripeSubscriptionId,
                   stripeSnapshot.StripeSubscriptionId,
                   StringComparison.Ordinal);
    }

    private static bool MatchesSubscriptionPeriod(
        UserSubscription subscription,
        DateTime expectedEndDate)
    {
        if (!subscription.EndDate.HasValue)
        {
            return false;
        }

        return subscription.EndDate.Value == expectedEndDate;
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

        if (string.Equals(normalized, "processing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "requires_action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "requires_payment_method", StringComparison.OrdinalIgnoreCase))
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
