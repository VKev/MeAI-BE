using Domain.Entities;
using Domain.Repositories;

namespace Application.Usage.Models;

internal static class AiUsageHistoryMapping
{
    public static AiUsageHistoryResponse ToResponse(
        AiSpendRecordHistoryPage page,
        IReadOnlyDictionary<Guid, AiUsageTiming> timings)
    {
        return new AiUsageHistoryResponse(
            page.Items.Select(record => ToItem(record, timings)).ToList(),
            page.NextCursorCreatedAt,
            page.NextCursorId);
    }

    private static AiUsageHistoryItemResponse ToItem(
        AiSpendRecord record,
        IReadOnlyDictionary<Guid, AiUsageTiming> timings)
    {
        timings.TryGetValue(record.Id, out var timing);
        timing ??= new AiUsageTiming(null, null, null);

        return new AiUsageHistoryItemResponse(
            SpendRecordId: record.Id,
            UserId: record.UserId,
            WorkspaceId: record.WorkspaceId,
            Provider: record.Provider,
            ActionType: record.ActionType,
            Model: record.Model,
            Variant: record.Variant,
            Unit: record.Unit,
            Quantity: record.Quantity,
            UnitCostCoins: record.UnitCostCoins,
            TotalCoins: record.TotalCoins,
            Status: record.Status,
            ReferenceType: record.ReferenceType,
            ReferenceId: record.ReferenceId,
            CreatedAt: record.CreatedAt,
            UpdatedAt: record.UpdatedAt,
            StartedAtUtc: timing.StartedAtUtc,
            CompletedAtUtc: timing.CompletedAtUtc,
            ProcessingDurationSeconds: timing.ProcessingDurationSeconds);
    }
}
