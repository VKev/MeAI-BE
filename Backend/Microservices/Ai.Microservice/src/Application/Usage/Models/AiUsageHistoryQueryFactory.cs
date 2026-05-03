using Domain.Repositories;
using SharedLibrary.Common.ResponseModel;

namespace Application.Usage.Models;

internal static class AiUsageHistoryQueryFactory
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    public static Result<AiSpendRecordHistoryQuery> Create(AiUsageHistoryFilter filter)
    {
        var errors = new List<Error>();

        if (filter.FromUtc.HasValue && filter.ToUtc.HasValue && filter.FromUtc.Value >= filter.ToUtc.Value)
        {
            errors.Add(new Error(
                "AiUsageHistory.InvalidDateRange",
                "fromUtc must be earlier than toUtc."));
        }

        if (filter.CursorCreatedAt.HasValue && !filter.CursorId.HasValue)
        {
            errors.Add(new Error(
                "AiUsageHistory.InvalidCursorId",
                "cursorId is required when cursorCreatedAt is provided."));
        }

        if (filter.CursorId.HasValue && !filter.CursorCreatedAt.HasValue)
        {
            errors.Add(new Error(
                "AiUsageHistory.InvalidCursorCreatedAt",
                "cursorCreatedAt is required when cursorId is provided."));
        }

        var limit = filter.Limit ?? DefaultLimit;
        if (limit <= 0)
        {
            errors.Add(new Error(
                "AiUsageHistory.InvalidLimit",
                "limit must be greater than 0."));
        }
        else if (limit > MaxLimit)
        {
            errors.Add(new Error(
                "AiUsageHistory.InvalidLimit",
                "limit must be less than or equal to 100."));
        }

        if (errors.Count > 0)
        {
            return ValidationResult<AiSpendRecordHistoryQuery>.WithErrors(errors.ToArray());
        }

        return Result.Success(new AiSpendRecordHistoryQuery(
            UserId: filter.UserId,
            FromUtc: filter.FromUtc,
            ToUtc: filter.ToUtc,
            ActionType: Normalize(filter.ActionType),
            Status: Normalize(filter.Status),
            WorkspaceId: filter.WorkspaceId,
            Provider: Normalize(filter.Provider),
            Model: Normalize(filter.Model),
            ReferenceType: Normalize(filter.ReferenceType),
            CursorCreatedAt: filter.CursorCreatedAt,
            CursorId: filter.CursorId,
            Limit: limit));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
