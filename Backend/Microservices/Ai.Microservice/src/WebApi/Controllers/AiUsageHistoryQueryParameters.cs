using System.Globalization;
using Application.Usage.Models;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

public sealed record AiUsageHistoryQueryParameters(
    string? FromUtc,
    string? ToUtc,
    string? ActionType,
    string? Status,
    string? WorkspaceId,
    string? Provider,
    string? Model,
    string? ReferenceType,
    string? CursorCreatedAt,
    string? CursorId,
    string? Limit,
    string? UserId = null)
{
    public Result<AiUsageHistoryFilter> ToFilter()
    {
        var errors = new List<Error>();

        var fromUtc = ParseDateTime(FromUtc, "fromUtc", errors);
        var toUtc = ParseDateTime(ToUtc, "toUtc", errors);
        var workspaceId = ParseGuid(WorkspaceId, "workspaceId", errors);
        var cursorCreatedAt = ParseDateTime(CursorCreatedAt, "cursorCreatedAt", errors);
        var cursorId = ParseGuid(CursorId, "cursorId", errors);
        var userId = ParseGuid(UserId, "userId", errors);
        var limit = ParseInt(Limit, "limit", errors);

        if (errors.Count > 0)
        {
            return ValidationResult<AiUsageHistoryFilter>.WithErrors(errors.ToArray());
        }

        return Result.Success(new AiUsageHistoryFilter(
            FromUtc: fromUtc,
            ToUtc: toUtc,
            ActionType: ActionType,
            Status: Status,
            WorkspaceId: workspaceId,
            Provider: Provider,
            Model: Model,
            ReferenceType: ReferenceType,
            CursorCreatedAt: cursorCreatedAt,
            CursorId: cursorId,
            Limit: limit,
            UserId: userId));
    }

    private static DateTime? ParseDateTime(string? rawValue, string fieldName, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (DateTime.TryParse(
            rawValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed))
        {
            return parsed;
        }

        errors.Add(new Error(
            $"AiUsageHistory.Invalid{ToPascalCase(fieldName)}",
            $"{fieldName} must be a valid UTC date time."));
        return null;
    }

    private static Guid? ParseGuid(string? rawValue, string fieldName, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (Guid.TryParse(rawValue, out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        errors.Add(new Error(
            $"AiUsageHistory.Invalid{ToPascalCase(fieldName)}",
            $"{fieldName} must be a valid GUID."));
        return null;
    }

    private static int? ParseInt(string? rawValue, string fieldName, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add(new Error(
            $"AiUsageHistory.Invalid{ToPascalCase(fieldName)}",
            $"{fieldName} must be a valid integer."));
        return null;
    }

    private static string ToPascalCase(string fieldName)
    {
        return string.Concat(fieldName.Select((ch, index) =>
            index == 0 ? char.ToUpperInvariant(ch).ToString() : ch.ToString()));
    }
}
