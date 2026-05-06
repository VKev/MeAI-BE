namespace Infrastructure.Logic.Seeding;

internal static class BillingSeedCatalog
{
    internal static IReadOnlyList<BillingSeedTier> Tiers { get; } =
    [
        new(
            SubscriptionName: "Subscription 10000",
            CoinAmount: 10000m,
            SubscriptionCostVnd: 100000m,
            SocialAccounts: 8,
            ContentRate: 5,
            MaxPages: 10,
            CoinPackageName: "Coin Package 10000",
            CoinPackageBonusCoins: 0m,
            CoinPackagePriceUsd: 3.99m,
            CoinPackageDisplayOrder: 1),
        new(
            SubscriptionName: "Subscription 15000",
            CoinAmount: 15000m,
            SubscriptionCostVnd: 150000m,
            SocialAccounts: 15,
            ContentRate: 10,
            MaxPages: 20,
            CoinPackageName: "Coin Package 15000",
            CoinPackageBonusCoins: 0m,
            CoinPackagePriceUsd: 5.99m,
            CoinPackageDisplayOrder: 2),
        new(
            SubscriptionName: "Subscription 20000",
            CoinAmount: 20000m,
            SubscriptionCostVnd: 200000m,
            SocialAccounts: 30,
            ContentRate: 20,
            MaxPages: 50,
            CoinPackageName: "Coin Package 20000",
            CoinPackageBonusCoins: 0m,
            CoinPackagePriceUsd: 7.99m,
            CoinPackageDisplayOrder: 3)
    ];
}

internal sealed record BillingSeedTier(
    string SubscriptionName,
    decimal CoinAmount,
    decimal SubscriptionCostVnd,
    int SocialAccounts,
    int ContentRate,
    int MaxPages,
    string CoinPackageName,
    decimal CoinPackageBonusCoins,
    decimal CoinPackagePriceUsd,
    int CoinPackageDisplayOrder);
