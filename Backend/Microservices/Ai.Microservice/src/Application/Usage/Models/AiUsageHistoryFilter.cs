namespace Application.Usage.Models;

public sealed record AiUsageHistoryFilter(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? ActionType,
    string? Status,
    Guid? WorkspaceId,
    string? Provider,
    string? Model,
    string? ReferenceType,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    Guid? UserId = null);
