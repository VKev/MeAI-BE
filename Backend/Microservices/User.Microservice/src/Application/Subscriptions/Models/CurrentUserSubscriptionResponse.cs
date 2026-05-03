namespace Application.Subscriptions.Models;

public sealed record CurrentUserSubscriptionResponse(
    Guid UserSubscriptionId,
    Guid SubscriptionId,
    string? SubscriptionName,
    DateTime? ActiveDate,
    DateTime? EndDate,
    string? Status,
    string DisplayStatus,
    bool IsCurrent,
    bool IsActive,
    bool IsScheduled,
    bool IsAutoRenewEnabled,
    string AutoRenewStatus);

public sealed record CurrentSubscriptionEntitlementsResponse(
    bool HasActivePlan,
    Guid? CurrentSubscriptionId,
    Guid? CurrentPlanId,
    string? CurrentPlanName,
    int MaxSocialAccounts,
    int CurrentSocialAccounts,
    int RemainingSocialAccounts,
    int MaxPagesPerSocialAccount,
    int CurrentWorkspaceCount,
    int MaxWorkspaces);
