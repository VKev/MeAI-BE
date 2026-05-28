namespace Application.Billing;

public static class GeneratedPostCoinCost
{
    public const decimal BaseCoins = 20m;
    public const decimal ExtraImageCoins = 0m;

    public static decimal Calculate(int requestedImageCount)
    {
        var normalizedImageCount = Math.Max(1, requestedImageCount);
        return BaseCoins + ((normalizedImageCount - 1) * ExtraImageCoins);
    }

    public static CoinCostQuote CreateQuote(
        string actionType,
        string model,
        string? variant,
        int requestedImageCount)
    {
        var totalCoins = Calculate(requestedImageCount);
        return new CoinCostQuote(
            ActionType: actionType,
            Model: model,
            Variant: variant,
            Unit: "per_request",
            UnitCostCoins: totalCoins,
            Quantity: 1,
            TotalCoins: totalCoins);
    }
}
