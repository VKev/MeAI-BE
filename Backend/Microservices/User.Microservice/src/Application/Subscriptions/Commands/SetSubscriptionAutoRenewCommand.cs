using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Subscriptions.Helpers;
using Application.Subscriptions.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record SetSubscriptionAutoRenewCommand(Guid UserId, bool Enabled)
    : IRequest<Result<CurrentUserSubscriptionResponse>>;

public sealed class SetSubscriptionAutoRenewCommandHandler
    : IRequestHandler<SetSubscriptionAutoRenewCommand, Result<CurrentUserSubscriptionResponse>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IUserSubscriptionStateService _userSubscriptionStateService;
    private readonly IStripePaymentService _stripePaymentService;

    public SetSubscriptionAutoRenewCommandHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionStateService userSubscriptionStateService,
        IStripePaymentService stripePaymentService)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userSubscriptionStateService = userSubscriptionStateService;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<CurrentUserSubscriptionResponse>> Handle(
        SetSubscriptionAutoRenewCommand request,
        CancellationToken cancellationToken)
    {
        var state = await _userSubscriptionStateService.GetStateAsync(
            request.UserId,
            cancellationToken,
            persistChanges: true);

        if (state.Current == null)
        {
            return Result.Failure<CurrentUserSubscriptionResponse>(
                new Error("Subscription.CurrentNotFound", "No active subscription found."));
        }

        var currentPlan = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == state.Current.SubscriptionId, cancellationToken);

        if (request.Enabled)
        {
            var eligibilityError = ValidateCanEnableAutoRenew(state.Current, currentPlan);
            if (eligibilityError != null)
            {
                return Result.Failure<CurrentUserSubscriptionResponse>(eligibilityError);
            }
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;

        if (!string.IsNullOrWhiteSpace(state.Current.StripeSubscriptionId))
        {
            var metadata = BuildStripeMetadata(request.UserId, state.Current.SubscriptionId, request.Enabled);
            try
            {
                var stripeResult = await _stripePaymentService.SetSubscriptionAutoRenewAsync(
                    state.Current.StripeSubscriptionId,
                    request.Enabled ? null : state.Scheduled?.StripeScheduleId ?? state.Current.StripeScheduleId,
                    request.Enabled,
                    metadata,
                    cancellationToken);

                state.Current.StripeScheduleId = string.IsNullOrWhiteSpace(stripeResult.StripeScheduleId)
                    ? null
                    : stripeResult.StripeScheduleId;

                if (stripeResult.CurrentPeriodEnd.HasValue)
                {
                    state.Current.EndDate = stripeResult.CurrentPeriodEnd;
                }
            }
            catch (Exception ex)
            {
                var code = request.Enabled
                    ? "Stripe.EnableAutoRenewFailed"
                    : "Stripe.DisableAutoRenewFailed";

                return Result.Failure<CurrentUserSubscriptionResponse>(
                    new Error(code, ex.Message));
            }
        }

        state.Current.Status = request.Enabled ? "Active" : "non_renewable";
        state.Current.UpdatedAt = now;
        _userSubscriptionRepository.Update(state.Current);

        if (!request.Enabled && state.Scheduled != null)
        {
            state.Scheduled.Status = "Superseded";
            state.Scheduled.EndDate = now;
            state.Scheduled.StripeScheduleId = null;
            state.Scheduled.UpdatedAt = now;
            _userSubscriptionRepository.Update(state.Scheduled);
        }

        return Result.Success(ToResponse(state.Current, currentPlan));
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

    private static CurrentUserSubscriptionResponse ToResponse(
        UserSubscription userSubscription,
        Subscription? subscription)
    {
        var autoRenewStatus = SubscriptionHelpers.ResolveAutoRenewStatus(
            userSubscription,
            subscription,
            isScheduled: false);

        return new CurrentUserSubscriptionResponse(
            userSubscription.Id,
            userSubscription.SubscriptionId,
            subscription?.Name,
            userSubscription.ActiveDate,
            userSubscription.EndDate,
            userSubscription.Status,
            SubscriptionHelpers.ResolveDisplayStatus(userSubscription.Status, subscription),
            true,
            true,
            false,
            autoRenewStatus == SubscriptionHelpers.AutoRenewEnabled,
            autoRenewStatus);
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
}
