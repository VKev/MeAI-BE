using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetCurrentUserSubscriptionQuery(Guid UserId)
    : IRequest<Result<CurrentUserSubscriptionResponse?>>;

public sealed class GetCurrentUserSubscriptionQueryHandler
    : IRequestHandler<GetCurrentUserSubscriptionQuery, Result<CurrentUserSubscriptionResponse?>>
{
    private readonly IUserSubscriptionStateService _userSubscriptionStateService;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public GetCurrentUserSubscriptionQueryHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionStateService userSubscriptionStateService)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionStateService = userSubscriptionStateService;
    }

    public async Task<Result<CurrentUserSubscriptionResponse?>> Handle(
        GetCurrentUserSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var state = await _userSubscriptionStateService.GetStateAsync(request.UserId, cancellationToken, persistChanges: true);
        if (state.Current == null)
        {
            return Result.Success<CurrentUserSubscriptionResponse?>(null);
        }

        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == state.Current.SubscriptionId && !item.IsDeleted,
                cancellationToken);

        var currentSubscription = new CurrentUserSubscriptionResponse(
            state.Current.Id,
            state.Current.SubscriptionId,
            subscription?.Name,
            state.Current.ActiveDate,
            state.Current.EndDate,
            state.Current.Status,
            true,
            true,
            false);

        return Result.Success<CurrentUserSubscriptionResponse?>(currentSubscription);
    }
}
