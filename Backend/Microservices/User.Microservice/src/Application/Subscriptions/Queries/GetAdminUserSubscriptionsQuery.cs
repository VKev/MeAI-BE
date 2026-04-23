using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Application.Subscriptions.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetAdminUserSubscriptionsQuery(
    Guid? UserId,
    string? Status,
    bool IncludeDeleted) : IRequest<Result<List<AdminUserSubscriptionResponse>>>;

public sealed class GetAdminUserSubscriptionsQueryHandler
    : IRequestHandler<GetAdminUserSubscriptionsQuery, Result<List<AdminUserSubscriptionResponse>>>
{
    private const string RelationTypeSubscription = "Subscription";

    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;

    public GetAdminUserSubscriptionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userRepository = unitOfWork.Repository<User>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<List<AdminUserSubscriptionResponse>>> Handle(
        GetAdminUserSubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _userSubscriptionRepository.GetAll().AsNoTracking();

        if (!request.IncludeDeleted)
        {
            query = query.Where(item => !item.IsDeleted);
        }

        if (request.UserId.HasValue)
        {
            query = query.Where(item => item.UserId == request.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            query = query.Where(item => item.Status != null && item.Status.ToLower() == status.ToLower());
        }

        var userSubscriptions = await query
            .OrderByDescending(item => item.CreatedAt ?? item.ActiveDate ?? item.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (userSubscriptions.Count == 0)
        {
            return Result.Success(new List<AdminUserSubscriptionResponse>());
        }

        var users = await BuildUserLookupAsync(userSubscriptions, cancellationToken);
        var subscriptions = await BuildSubscriptionLookupAsync(userSubscriptions, cancellationToken);
        var payments = await BuildLatestPaymentLookupAsync(userSubscriptions, cancellationToken);

        var response = userSubscriptions
            .Select(item =>
            {
                users.TryGetValue(item.UserId, out var user);
                subscriptions.TryGetValue(item.SubscriptionId, out var subscription);
                payments.TryGetValue((item.UserId, item.SubscriptionId), out var payment);

                return ToResponse(item, user, subscription, payment);
            })
            .ToList();

        return Result.Success(response);
    }

    private async Task<Dictionary<Guid, User>> BuildUserLookupAsync(
        IEnumerable<UserSubscription> userSubscriptions,
        CancellationToken cancellationToken)
    {
        var userIds = userSubscriptions
            .Select(item => item.UserId)
            .Distinct()
            .ToList();

        return await _userRepository.GetAll()
            .AsNoTracking()
            .Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, Subscription>> BuildSubscriptionLookupAsync(
        IEnumerable<UserSubscription> userSubscriptions,
        CancellationToken cancellationToken)
    {
        var subscriptionIds = userSubscriptions
            .Select(item => item.SubscriptionId)
            .Distinct()
            .ToList();

        return await _subscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(item => subscriptionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
    }

    private async Task<Dictionary<(Guid UserId, Guid SubscriptionId), Transaction>> BuildLatestPaymentLookupAsync(
        IEnumerable<UserSubscription> userSubscriptions,
        CancellationToken cancellationToken)
    {
        var userIds = userSubscriptions
            .Select(item => item.UserId)
            .Distinct()
            .ToList();

        var subscriptionIds = userSubscriptions
            .Select(item => item.SubscriptionId)
            .Distinct()
            .ToList();

        var transactions = await _transactionRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                !item.IsDeleted &&
                item.RelationId.HasValue &&
                item.RelationType != null &&
                item.RelationType.ToLower() == RelationTypeSubscription.ToLower() &&
                userIds.Contains(item.UserId) &&
                subscriptionIds.Contains(item.RelationId.Value))
            .ToListAsync(cancellationToken);

        return transactions
            .GroupBy(item => (item.UserId, SubscriptionId: item.RelationId!.Value))
            .ToDictionary(
                item => item.Key,
                item => item
                    .OrderByDescending(transaction => transaction.CreatedAt ?? transaction.UpdatedAt)
                    .First());
    }

    internal static AdminUserSubscriptionResponse ToResponse(
        UserSubscription userSubscription,
        User? user,
        Subscription? subscription,
        Transaction? payment)
    {
        return new AdminUserSubscriptionResponse(
            userSubscription.Id,
            userSubscription.UserId,
            user?.Username,
            user?.Email,
            userSubscription.SubscriptionId,
            subscription?.Name,
            payment?.Cost ?? (subscription?.Cost.HasValue == true ? Convert.ToDecimal(subscription.Cost.Value) : null),
            subscription?.Cost,
            subscription?.DurationMonths ?? 0,
            subscription?.MeAiCoin,
            userSubscription.Status,
            SubscriptionHelpers.ResolveDisplayStatus(userSubscription.Status, subscription),
            subscription?.IsActive ?? false,
            subscription?.IsDeleted ?? false,
            userSubscription.ActiveDate,
            userSubscription.EndDate,
            userSubscription.IsDeleted,
            userSubscription.CreatedAt,
            userSubscription.UpdatedAt,
            userSubscription.DeletedAt,
            userSubscription.StripeSubscriptionId,
            userSubscription.StripeScheduleId);
    }
}
