namespace Application.Usage.Models;

public sealed record AiUsageHistoryResponse(
    IReadOnlyList<AiUsageHistoryItemResponse> Items,
    DateTime? NextCursorCreatedAt,
    Guid? NextCursorId);

public sealed record AiUsageHistoryItemResponse(
    Guid SpendRecordId,
    Guid UserId,
    Guid? WorkspaceId,
    string Provider,
    string ActionType,
    string Model,
    string? Variant,
    string Unit,
    int Quantity,
    decimal UnitCostCoins,
    decimal TotalCoins,
    string Status,
    string ReferenceType,
    string ReferenceId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int? ProcessingDurationSeconds);
