using Application.Billing;
using Domain.Repositories;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Billing;

public sealed class CoinPricingService : ICoinPricingService
{
    private readonly ICoinPricingRepository _repository;

    public CoinPricingService(ICoinPricingRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<CoinCostQuote>> GetCostAsync(
        string actionType,
        string model,
        string? variant,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            return Result.Failure<CoinCostQuote>(
                new Error("Pricing.InvalidQuantity", "Quantity must be positive."));
        }

        var entry = await _repository.ResolveAsync(actionType, model, variant, cancellationToken);
        if (entry is null)
        {
            return Result.Failure<CoinCostQuote>(
                new Error(
                    "Pricing.NotFound",
                    $"No active pricing entry for action={actionType}, model={model}, variant={variant ?? "null"}."));
        }

        var total = entry.UnitCostCoins * quantity;
        return Result.Success(new CoinCostQuote(
            entry.ActionType,
            entry.Model,
            entry.Variant,
            entry.Unit,
            entry.UnitCostCoins,
            quantity,
            total));
    }
}
