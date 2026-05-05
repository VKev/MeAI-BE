using Domain.Entities;

namespace Domain.Repositories;

public sealed record AiSpendRecordHistoryQuery(
    Guid? UserId,
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
    int Limit);

public sealed record AiSpendRecordHistoryPage(
    IReadOnlyList<AiSpendRecord> Items,
    DateTime? NextCursorCreatedAt,
    Guid? NextCursorId);
