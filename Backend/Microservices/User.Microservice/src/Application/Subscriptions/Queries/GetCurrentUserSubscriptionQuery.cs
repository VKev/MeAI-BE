using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Queries;

public sealed record GetCurrentUserSubscriptionQuery(Guid UserId)
    : IRequest<Result<CurrentUserSubscriptionResponse?>>;

public sealed class GetCurrentUserSubscriptionQueryHandler
    : IRequestHandler<GetCurrentUserSubscriptionQuery, Result<CurrentUserSubscriptionResponse?>>
{
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public GetCurrentUserSubscriptionQueryHandler(IUnitOfWork unitOfWork)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<CurrentUserSubscriptionResponse?>> Handle(
        GetCurrentUserSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var currentSubscription = await (
            from userSubscription in _userSubscriptionRepository.GetAll().AsNoTracking()
            join subscription in _subscriptionRepository.GetAll().AsNoTracking()
                on userSubscription.SubscriptionId equals subscription.Id
            where
                userSubscription.UserId == request.UserId &&
                !userSubscription.IsDeleted &&
                !subscription.IsDeleted &&
                (!userSubscription.EndDate.HasValue || userSubscription.EndDate.Value >= now)
            orderby userSubscription.EndDate descending, userSubscription.CreatedAt descending
            select new CurrentUserSubscriptionResponse(
                userSubscription.Id,
                userSubscription.SubscriptionId,
                subscription.Name,
                userSubscription.ActiveDate,
                userSubscription.EndDate,
                userSubscription.Status,
                !userSubscription.EndDate.HasValue || userSubscription.EndDate.Value >= now))
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(currentSubscription);
    }
}
