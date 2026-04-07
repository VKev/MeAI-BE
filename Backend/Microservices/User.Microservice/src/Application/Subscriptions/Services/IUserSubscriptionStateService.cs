using Domain.Entities;

namespace Application.Subscriptions.Services;

public interface IUserSubscriptionStateService
{
    Task<UserSubscriptionState> GetStateAsync(
        Guid userId,
        CancellationToken cancellationToken,
        bool persistChanges = false);
}

public sealed record UserSubscriptionState(
    UserSubscription? Current,
    UserSubscription? Scheduled);
