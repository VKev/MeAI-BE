namespace Application.Transactions.Models;

public sealed record TransactionResponse(
    Guid Id,
    Guid UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt,
    bool IsDeleted,
    TransactionRelationInfo? Relation,
    TransactionUserSummaryResponse? User);

public sealed record TransactionRelationInfo(
    string Type,
    Guid Id,
    TransactionSubscriptionRelationResponse? Subscription);

public sealed record TransactionSubscriptionRelationResponse(
    Guid Id,
    string? Name,
    float? Cost,
    int DurationMonths,
    decimal? MeAiCoin);

public sealed record TransactionUserSummaryResponse(
    Guid Id,
    string Username,
    string Email,
    string? FullName,
    bool IsDeleted);
