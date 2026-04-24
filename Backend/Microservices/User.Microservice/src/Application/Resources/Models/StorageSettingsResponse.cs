namespace Application.Resources.Models;

public sealed record StorageSettingsResponse(
    long FreeStorageQuotaBytes,
    decimal FreeStorageQuotaMb,
    DateTime? UpdatedAt);
