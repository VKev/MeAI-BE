using Application.Abstractions.Data;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetStoragePlansQuery : IRequest<Result<IReadOnlyList<StoragePlanResponse>>>;

public sealed class GetStoragePlansQueryHandler
    : IRequestHandler<GetStoragePlansQuery, Result<IReadOnlyList<StoragePlanResponse>>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<Resource> _resourceRepository;

    public GetStoragePlansQueryHandler(IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _resourceRepository = unitOfWork.Repository<Resource>();
    }

    public async Task<Result<IReadOnlyList<StoragePlanResponse>>> Handle(
        GetStoragePlansQuery request,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(subscription => !subscription.IsDeleted)
            .OrderBy(subscription => subscription.Name)
            .ToListAsync(cancellationToken);

        var responses = new List<StoragePlanResponse>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            var activeUserIds = await _userSubscriptionRepository.GetAll()
                .AsNoTracking()
                .Where(item =>
                    item.SubscriptionId == subscription.Id &&
                    !item.IsDeleted &&
                    (item.Status == null || item.Status.ToLower() == "active" || item.Status.ToLower() == "non_renewable"))
                .Select(item => item.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var usersOverQuota = 0;
            if (subscription.Limits?.StorageQuotaBytes is long quotaBytes)
            {
                foreach (var userId in activeUserIds)
                {
                    var usedBytes = await _resourceRepository.GetAll()
                        .AsNoTracking()
                        .Where(resource => resource.UserId == userId && !resource.IsDeleted)
                        .SumAsync(resource => resource.SizeBytes ?? 0L, cancellationToken);

                    if (usedBytes > quotaBytes)
                    {
                        usersOverQuota++;
                    }
                }
            }

            responses.Add(new StoragePlanResponse(
                subscription.Id,
                subscription.Name,
                subscription.IsActive,
                subscription.Limits?.StorageQuotaBytes,
                subscription.Limits?.MaxUploadFileBytes,
                subscription.Limits?.RetentionDaysAfterDelete,
                activeUserIds.Count,
                usersOverQuota));
        }

        return Result.Success<IReadOnlyList<StoragePlanResponse>>(responses);
    }
}

public sealed record GetStoragePlanByIdQuery(Guid SubscriptionId) : IRequest<Result<StoragePlanResponse>>;

public sealed class GetStoragePlanByIdQueryHandler
    : IRequestHandler<GetStoragePlanByIdQuery, Result<StoragePlanResponse>>
{
    private readonly IMediator _mediator;

    public GetStoragePlanByIdQueryHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<Result<StoragePlanResponse>> Handle(
        GetStoragePlanByIdQuery request,
        CancellationToken cancellationToken)
    {
        var plansResult = await _mediator.Send(new GetStoragePlansQuery(), cancellationToken);
        if (plansResult.IsFailure)
        {
            return Result.Failure<StoragePlanResponse>(plansResult.Error);
        }

        var plan = plansResult.Value.FirstOrDefault(item => item.SubscriptionId == request.SubscriptionId);
        return plan is null
            ? Result.Failure<StoragePlanResponse>(new Error("Subscription.NotFound", "Subscription not found."))
            : Result.Success(plan);
    }
}
