using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record ToggleSubscriptionStatusCommand(
    Guid Id,
    bool IsActive) : IRequest<Result<Subscription>>;

public sealed class ToggleSubscriptionStatusCommandHandler
    : IRequestHandler<ToggleSubscriptionStatusCommand, Result<Subscription>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;

    public ToggleSubscriptionStatusCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
    }

    public async Task<Result<Subscription>> Handle(
        ToggleSubscriptionStatusCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        subscription.IsActive = request.IsActive;
        subscription.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _subscriptionRepository.Update(subscription);

        // When deactivating a plan, disable recurring for all active subscribers
        // so they only keep the plan until their current period ends
        if (!request.IsActive)
        {
            var activeUserSubscriptions = await _userSubscriptionRepository.GetAll()
                .Where(us =>
                    us.SubscriptionId == request.Id &&
                    !us.IsDeleted &&
                    us.Status == "active")
                .ToListAsync(cancellationToken);

            foreach (var userSub in activeUserSubscriptions)
            {
                // Mark as non-renewable — user keeps access until EndDate
                userSub.Status = "non_renewable";
                userSub.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _userSubscriptionRepository.Update(userSub);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(subscription);
    }
}
