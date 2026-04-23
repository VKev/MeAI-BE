using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Application.Subscriptions.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetMySubscriptionsQuery(Guid UserId)
    : IRequest<Result<List<CurrentUserSubscriptionResponse>>>;

public sealed class GetMySubscriptionsQueryHandler
    : IRequestHandler<GetMySubscriptionsQuery, Result<List<CurrentUserSubscriptionResponse>>>
{
    private readonly IUserSubscriptionStateService _userSubscriptionStateService;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public GetMySubscriptionsQueryHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionStateService userSubscriptionStateService)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionStateService = userSubscriptionStateService;
    }

    public async Task<Result<List<CurrentUserSubscriptionResponse>>> Handle(
        GetMySubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        var state = await _userSubscriptionStateService.GetStateAsync(request.UserId, cancellationToken, persistChanges: true);
        var subscriptionIds = new[] { state.Current?.SubscriptionId, state.Scheduled?.SubscriptionId }
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();

        if (subscriptionIds.Count == 0)
        {
            return Result.Success(new List<CurrentUserSubscriptionResponse>());
        }

        var subscriptions = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(item => subscriptionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var response = new List<CurrentUserSubscriptionResponse>();

        if (state.Current != null)
        {
            subscriptions.TryGetValue(state.Current.SubscriptionId, out var currentPlan);
            var autoRenewStatus = SubscriptionHelpers.ResolveAutoRenewStatus(
                state.Current,
                currentPlan,
                isScheduled: false);

            response.Add(new CurrentUserSubscriptionResponse(
                state.Current.Id,
                state.Current.SubscriptionId,
                currentPlan?.Name,
                state.Current.ActiveDate,
                state.Current.EndDate,
                state.Current.Status,
                SubscriptionHelpers.ResolveDisplayStatus(state.Current.Status, currentPlan),
                true,
                true,
                false,
                autoRenewStatus == SubscriptionHelpers.AutoRenewEnabled,
                autoRenewStatus));
        }

        if (state.Scheduled != null)
        {
            subscriptions.TryGetValue(state.Scheduled.SubscriptionId, out var scheduledPlan);
            var autoRenewStatus = SubscriptionHelpers.ResolveAutoRenewStatus(
                state.Scheduled,
                scheduledPlan,
                isScheduled: true);

            response.Add(new CurrentUserSubscriptionResponse(
                state.Scheduled.Id,
                state.Scheduled.SubscriptionId,
                scheduledPlan?.Name,
                state.Scheduled.ActiveDate,
                state.Scheduled.EndDate,
                state.Scheduled.Status,
                SubscriptionHelpers.ResolveDisplayStatus(state.Scheduled.Status, scheduledPlan),
                false,
                false,
                true,
                autoRenewStatus == SubscriptionHelpers.AutoRenewEnabled,
                autoRenewStatus));
        }

        return Result.Success(response);
    }
}
