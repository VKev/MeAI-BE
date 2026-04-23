using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Subscriptions.Commands;

public sealed record DeleteSubscriptionCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteSubscriptionCommandHandler : IRequestHandler<DeleteSubscriptionCommand, Result<bool>>
{
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;

    public DeleteSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = unitOfWork.Repository<Subscription>();
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
    }

    public async Task<Result<bool>> Handle(DeleteSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null || subscription.IsDeleted)
        {
            return Result.Failure<bool>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        subscription.IsActive = false;
        subscription.IsDeleted = true;
        subscription.DeletedAt = now;
        subscription.UpdatedAt = now;
        _subscriptionRepository.Update(subscription);

        var currentUserSubscriptions = await _userSubscriptionRepository.GetAll()
            .Where(item =>
                item.SubscriptionId == request.Id &&
                !item.IsDeleted &&
                (item.Status == null || item.Status.ToLower() == "active"))
            .ToListAsync(cancellationToken);

        foreach (var userSubscription in currentUserSubscriptions)
        {
            userSubscription.Status = "non_renewable";
            userSubscription.UpdatedAt = now;
            _userSubscriptionRepository.Update(userSubscription);
        }

        return Result.Success(true);
    }
}
