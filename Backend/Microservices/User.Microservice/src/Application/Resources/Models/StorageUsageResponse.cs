namespace Application.Resources.Models;

public sealed record StorageUsageResponse(
    Guid? UserId,
    Guid? SubscriptionId,
    string? SubscriptionName,
    long? QuotaBytes,
    long UsedBytes,
    long ReservedBytes,
    long? AvailableBytes,
    decimal? UsagePercent,
    long? MaxUploadFileBytes,
    bool IsOverQuota);
