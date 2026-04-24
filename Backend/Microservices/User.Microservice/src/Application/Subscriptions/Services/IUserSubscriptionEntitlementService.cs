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
    private const int FreeTierMaxSocialAccounts = 2;
    private const int FreeTierMaxWorkspaces = int.MaxValue;
    private const int FreeTierMaxPagesPerSocialAccount = 5;
    public const long DefaultFreeStorageQuotaBytes = 100L * 1024L * 1024L;

    public bool HasActivePlan => CurrentSubscription != null && CurrentPlan != null;

    public int MaxWorkspaces => CurrentPlan?.Limits?.NumberOfWorkspaces ?? FreeTierMaxWorkspaces;

    public int MaxSocialAccounts => CurrentPlan?.Limits?.NumberOfSocialAccounts ?? FreeTierMaxSocialAccounts;

    public int MaxPagesPerSocialAccount => CurrentPlan?.Limits?.MaxPagesPerSocialAccount ?? FreeTierMaxPagesPerSocialAccount;

    public decimal CoinAllowance => CurrentPlan?.MeAiCoin ?? 0m;

    public long? StorageQuotaBytes(long? freeStorageQuotaBytes) =>
        CurrentPlan?.Limits?.StorageQuotaBytes ?? freeStorageQuotaBytes ?? DefaultFreeStorageQuotaBytes;

    public long? MaxUploadFileBytes => CurrentPlan?.Limits?.MaxUploadFileBytes;
}
