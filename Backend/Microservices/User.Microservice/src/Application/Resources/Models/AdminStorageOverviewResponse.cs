namespace Application.Resources.Models;

public sealed record AdminStorageOverviewResponse(
    DateTime GeneratedAtUtc,
    int TotalStoredFiles,
    long TotalStorageBytes,
    decimal TotalStorageSizeGb,
    int UnresolvedFileCount,
    IReadOnlyList<AdminStorageBreakdownResponse> StorageByResourceType,
    IReadOnlyList<AdminStorageBreakdownResponse> StorageByContentType);

public sealed record AdminStorageBreakdownResponse(
    string Key,
    string Label,
    int FileCount,
    long TotalBytes,
    decimal TotalSizeGb);
