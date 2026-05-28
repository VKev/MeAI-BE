namespace Infrastructure.Logic.Seeding;

internal static class BillingSeedCatalog
{
    internal static IReadOnlyList<BillingSeedTier> Tiers { get; } =
    [
        new(
            SubscriptionName: "Plus",
            LegacySubscriptionNames: ["Plus"],
            CoinAmount: 100m,
            SubscriptionCostVnd: 100000m,
            SocialAccounts: 8,
            Workspaces: null,
            ContentRate: 5,
            MaxPages: 10,
            StorageQuotaBytes: 2L * 1024L * 1024L * 1024L,
            CoinPackageName: "Plus Coins",
            LegacyCoinPackageNames: ["Plus Coins"],
            CoinPackageBonusCoins: 0m,
            CoinPackagePrice: 100000m,
            CoinPackageDisplayOrder: 1),
        new(
            SubscriptionName: "Pro",
            LegacySubscriptionNames: ["Pro"],
            CoinAmount: 160m,
            SubscriptionCostVnd: 150000m,
            SocialAccounts: 15,
            Workspaces: null,
            ContentRate: 10,
            MaxPages: 20,
            StorageQuotaBytes: 10L * 1024L * 1024L * 1024L,
            CoinPackageName: "Pro Coins",
            LegacyCoinPackageNames: ["Pro Coins"],
            CoinPackageBonusCoins: 0m,
            CoinPackagePrice: 150000m,
            CoinPackageDisplayOrder: 2),
        new(
            SubscriptionName: "Pro Max",
            LegacySubscriptionNames: ["Pro Max"],
            CoinAmount: 220m,
            SubscriptionCostVnd: 200000m,
            SocialAccounts: 30,
            Workspaces: null,
            ContentRate: 20,
            MaxPages: 50,
            StorageQuotaBytes: 20L * 1024L * 1024L * 1024L,
            CoinPackageName: "Pro Max",
            LegacyCoinPackageNames: ["Pro Max"],
            CoinPackageBonusCoins: 0m,
            CoinPackagePrice: 200000m,
            CoinPackageDisplayOrder: 3)
    ];
}

internal sealed record BillingSeedTier(
    string SubscriptionName,
    IReadOnlyList<string> LegacySubscriptionNames,
    decimal CoinAmount,
    decimal SubscriptionCostVnd,
    int SocialAccounts,
    int? Workspaces,
    int ContentRate,
    int MaxPages,
    long StorageQuotaBytes,
    string CoinPackageName,
    IReadOnlyList<string> LegacyCoinPackageNames,
    decimal CoinPackageBonusCoins,
    decimal CoinPackagePrice,
    int CoinPackageDisplayOrder);
