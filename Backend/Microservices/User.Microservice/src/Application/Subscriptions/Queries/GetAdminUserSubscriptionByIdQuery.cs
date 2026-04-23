using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetAdminUserSubscriptionByIdQuery(
    Guid UserSubscriptionId,
    bool IncludeDeleted) : IRequest<Result<AdminUserSubscriptionResponse>>;

public sealed class GetAdminUserSubscriptionByIdQueryHandler
    : IRequestHandler<GetAdminUserSubscriptionByIdQuery, Result<AdminUserSubscriptionResponse>>
{
    private const string RelationTypeSubscription = "Subscription";

    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;

    public GetAdminUserSubscriptionByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userRepository = unitOfWork.Repository<User>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<AdminUserSubscriptionResponse>> Handle(
        GetAdminUserSubscriptionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = _userSubscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.Id == request.UserSubscriptionId);

        if (!request.IncludeDeleted)
        {
            query = query.Where(item => !item.IsDeleted);
        }

        var userSubscription = await query.FirstOrDefaultAsync(cancellationToken);
        if (userSubscription == null)
        {
            return Result.Failure<AdminUserSubscriptionResponse>(
                new Error("UserSubscription.NotFound", "User subscription not found."));
        }

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userSubscription.UserId, cancellationToken);

        var subscription = await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userSubscription.SubscriptionId, cancellationToken);

        var payment = await _transactionRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.UserId == userSubscription.UserId &&
                item.RelationId == userSubscription.SubscriptionId &&
                item.RelationType != null &&
                item.RelationType.ToLower() == RelationTypeSubscription.ToLower())
            .OrderByDescending(item => item.CreatedAt ?? item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(GetAdminUserSubscriptionsQueryHandler.ToResponse(
            userSubscription,
            user,
            subscription,
            payment));
    }
}
