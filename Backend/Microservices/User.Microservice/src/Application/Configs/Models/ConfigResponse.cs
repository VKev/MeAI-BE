namespace Application.Configs.Models;

public sealed record ConfigResponse(
    Guid Id,
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances,
    long? FreeStorageQuotaBytes,
    long? SystemStorageQuotaBytes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
