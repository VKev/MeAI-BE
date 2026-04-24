namespace Application.Configs.Models;

public sealed record ConfigResponse(
    Guid Id,
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances,
    long? FreeStorageQuotaBytes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
