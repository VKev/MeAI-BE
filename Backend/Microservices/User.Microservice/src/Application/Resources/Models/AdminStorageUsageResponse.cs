namespace Application.Resources.Models;

public sealed record AdminStorageUsageResponse(
    long TotalUsedBytes,
    int TotalResourceCount,
    IReadOnlyList<AdminStorageUserUsageResponse> Users);

public sealed record AdminStorageUserUsageResponse(
    Guid UserId,
    string? Email,
    Guid? SubscriptionId,
    string? SubscriptionName,
    long? QuotaBytes,
    long UsedBytes,
    long? AvailableBytes,
    decimal? UsagePercent,
    bool IsOverQuota,
    int ResourceCount);
