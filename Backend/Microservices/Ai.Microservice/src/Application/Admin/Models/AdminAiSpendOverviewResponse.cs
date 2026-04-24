namespace Application.Admin.Models;

public sealed record AdminAiSpendOverviewResponse(
    string Source,
    string Currency,
    decimal CoinUsdRate,
    string SelectedPeriod,
    DateTime GeneratedAtUtc,
    IReadOnlyList<AdminExternalProviderCreditResponse> ExternalProviderCredits,
    IReadOnlyList<AdminAiSpendPeriodTotalResponse> Totals,
    IReadOnlyList<AdminAiSpendBreakdownResponse> SpendByAction,
    IReadOnlyList<AdminAiSpendBreakdownResponse> SpendByModel);

public sealed record AdminExternalProviderCreditResponse(
    string Provider,
    string Currency,
    decimal? RemainingCredits,
    bool IsAvailable,
    string? Message,
    DateTime CheckedAtUtc);

public sealed record AdminAiSpendPeriodTotalResponse(
    string Period,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalCoins,
    decimal EstimatedUsd,
    decimal GrossCoins,
    decimal RefundedCoins);

public sealed record AdminAiSpendBreakdownResponse(
    string Key,
    string Label,
    int Quantity,
    decimal TotalCoins,
    decimal EstimatedUsd,
    decimal GrossCoins,
    decimal RefundedCoins);
