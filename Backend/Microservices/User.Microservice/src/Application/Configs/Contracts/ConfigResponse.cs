namespace Application.Configs.Contracts;

public sealed record ConfigResponse(
    Guid Id,
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
