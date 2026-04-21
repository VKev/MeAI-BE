using SharedLibrary.Common.ResponseModel;

namespace Application.Billing;

public interface ICoinPricingService
{
    // Resolves the priced catalog entry and multiplies by `quantity`. Returns failure
    // ("Pricing.NotFound") if no active entry covers (action, model, variant).
    Task<Result<CoinCostQuote>> GetCostAsync(
        string actionType,
        string model,
        string? variant,
        int quantity,
        CancellationToken cancellationToken);
}

public sealed record CoinCostQuote(
    string ActionType,
    string Model,
    string? Variant,
    string Unit,
    decimal UnitCostCoins,
    int Quantity,
    decimal TotalCoins);
