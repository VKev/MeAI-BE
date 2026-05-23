namespace Application.Usage.Models;

public sealed record AiUsageSummaryResponse(
    string Period,
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime GeneratedAtUtc,
    AiUsageSummaryTotals Totals,
    IReadOnlyList<AiUsageSummaryBreakdownItem> SpendByAction,
    IReadOnlyList<AiUsageSummaryBreakdownItem> SpendByModel);

public sealed record AiUsageSummaryTotals(
    decimal GrossCoins,
    decimal RefundedCoins,
    decimal NetCoins,
    int TotalRequests);

public sealed record AiUsageSummaryBreakdownItem(
    string Key,
    string Label,
    int Quantity,
    decimal GrossCoins,
    decimal RefundedCoins,
    decimal NetCoins);
