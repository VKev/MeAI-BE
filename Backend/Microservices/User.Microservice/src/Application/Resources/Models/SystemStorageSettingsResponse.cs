namespace Application.Resources.Models;

public sealed record SystemStorageSettingsResponse(
    long? SystemStorageQuotaBytes,
    decimal? SystemStorageQuotaGb,
    long UsedBytes,
    decimal UsedGb,
    long? AvailableBytes,
    decimal? AvailableGb,
    decimal? UsagePercent,
    int ResourceCount,
    int UserCount,
    DateTime? UpdatedAt);
