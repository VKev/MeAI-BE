using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Application.Subscriptions.Queries;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record UpdateUserSubscriptionStatusCommand(
    Guid UserSubscriptionId,
    string Status) : IRequest<Result<AdminUserSubscriptionResponse>>;

public sealed class UpdateUserSubscriptionStatusCommandHandler
    : IRequestHandler<UpdateUserSubscriptionStatusCommand, Result<AdminUserSubscriptionResponse>>
{
    private const string RelationTypeSubscription = "Subscription";

    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<Transaction> _transactionRepository;

    public UpdateUserSubscriptionStatusCommandHandler(IUnitOfWork unitOfWork)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _userRepository = unitOfWork.Repository<User>();
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _transactionRepository = unitOfWork.Repository<Transaction>();
    }

    public async Task<Result<AdminUserSubscriptionResponse>> Handle(
        UpdateUserSubscriptionStatusCommand request,
        CancellationToken cancellationToken)
    {
        var userSubscription = await _userSubscriptionRepository.GetAll()
            .FirstOrDefaultAsync(
                item => item.Id == request.UserSubscriptionId && !item.IsDeleted,
                cancellationToken);

        if (userSubscription == null)
        {
            return Result.Failure<AdminUserSubscriptionResponse>(
                new Error("UserSubscription.NotFound", "User subscription not found."));
        }

        userSubscription.Status = NormalizeStatus(request.Status);
        userSubscription.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

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

    private static string NormalizeStatus(string status)
    {
        var normalized = status.Trim();

        return normalized.ToLowerInvariant() switch
        {
            "active" => "Active",
            "scheduled" => "Scheduled",
            "expired" => "Expired",
            "superseded" => "Superseded",
            "non_renewable" or "non-renewable" or "nonrenewable" => "non_renewable",
            _ => normalized
        };
    }
}
