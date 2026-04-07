using Domain.Entities;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Services;

public interface IUserSubscriptionEntitlementService
{
    Task<UserSubscriptionEntitlement> GetCurrentEntitlementAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Result<UserSubscriptionEntitlement>> EnsureWorkspaceCreationAllowedAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Result<UserSubscriptionEntitlement>> EnsureSocialAccountLinkAllowedAsync(
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record UserSubscriptionEntitlement(
    UserSubscription? CurrentSubscription,
    Subscription? CurrentPlan)
{
    public bool HasActivePlan => CurrentSubscription != null && CurrentPlan != null;

    public int MaxWorkspaces => CurrentPlan?.Limits?.NumberOfWorkspaces ?? 0;

    public int MaxSocialAccounts => CurrentPlan?.Limits?.NumberOfSocialAccounts ?? 0;

    public decimal CoinAllowance => CurrentPlan?.MeAiCoin ?? 0m;
}
