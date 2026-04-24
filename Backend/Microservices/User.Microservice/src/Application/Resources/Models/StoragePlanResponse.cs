namespace Application.Resources.Models;

public sealed record StoragePlanResponse(
    Guid SubscriptionId,
    string? SubscriptionName,
    bool IsActive,
    long? StorageQuotaBytes,
    long? MaxUploadFileBytes,
    int? RetentionDaysAfterDelete,
    int ActiveUserCount,
    int UsersOverQuotaCount);
