namespace Application.Resources.Models;

public sealed record AdminStorageUsageResponse(
    string? Namespace,
    long TotalUsedBytes,
    long TotalReservedBytes,
    int TotalResourceCount,
    IReadOnlyList<AdminStorageUserUsageResponse> Users);

public sealed record AdminStorageUserUsageResponse(
    Guid UserId,
    string? Email,
    Guid? SubscriptionId,
    string? SubscriptionName,
    long? QuotaBytes,
    long UsedBytes,
    long ReservedBytes,
    long? AvailableBytes,
    decimal? UsagePercent,
    bool IsOverQuota,
    int ResourceCount);
