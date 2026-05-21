using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Configs;

namespace Infrastructure.Logic.Seeding;

public sealed class CoinPackageSeeder
{
    private readonly MyDbContext _dbContext;
    private readonly ILogger<CoinPackageSeeder> _logger;
    private readonly string _configuredCurrency;

    public CoinPackageSeeder(
        MyDbContext dbContext,
        ILogger<CoinPackageSeeder> logger,
        IOptions<BillingCurrencyOptions> billingCurrencyOptions)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuredCurrency = ResolveCurrency(billingCurrencyOptions.Value);
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingPackages = await _dbContext.CoinPackages
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var toAdd = new List<CoinPackage>();
        var updatedCount = 0;

        foreach (var seed in BillingSeedCatalog.Tiers)
        {
            var existing = existingPackages.FirstOrDefault(package =>
                MatchesName(package.Name, seed.CoinPackageName, seed.LegacyCoinPackageNames));
            if (existing is not null)
            {
                if (ApplySeededValues(existing, seed, _configuredCurrency, now))
                {
                    updatedCount++;
                }

                continue;
            }

            toAdd.Add(new CoinPackage
            {
                Id = Guid.NewGuid(),
                Name = seed.CoinPackageName,
                CoinAmount = seed.CoinAmount,
                BonusCoins = seed.CoinPackageBonusCoins,
                Price = seed.CoinPackagePrice,
                Currency = _configuredCurrency,
                IsActive = true,
                DisplayOrder = seed.CoinPackageDisplayOrder,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (toAdd.Count == 0 && updatedCount == 0)
        {
            _logger.LogInformation("Coin package seed skipped; coin packages already match seed data.");
            return;
        }

        if (toAdd.Count > 0)
        {
            _dbContext.CoinPackages.AddRange(toAdd);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {AddedCount} coin package(s), updated {UpdatedCount} coin package(s).",
            toAdd.Count,
            updatedCount);
    }

    private static string ResolveCurrency(BillingCurrencyOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Currency)
            ? "vnd"
            : options.Currency.Trim().ToLowerInvariant();
    }

    private static bool ApplySeededValues(
        CoinPackage package,
        BillingSeedTier seed,
        string configuredCurrency,
        DateTime updatedAt)
    {
        var changed = false;

        if (!string.Equals(package.Name, seed.CoinPackageName, StringComparison.Ordinal))
        {
            package.Name = seed.CoinPackageName;
            changed = true;
        }

        if (package.CoinAmount != seed.CoinAmount)
        {
            package.CoinAmount = seed.CoinAmount;
            changed = true;
        }

        if (package.BonusCoins != seed.CoinPackageBonusCoins)
        {
            package.BonusCoins = seed.CoinPackageBonusCoins;
            changed = true;
        }

        if (package.Price != seed.CoinPackagePrice)
        {
            package.Price = seed.CoinPackagePrice;
            changed = true;
        }

        if (!string.Equals(package.Currency, configuredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            package.Currency = configuredCurrency;
            changed = true;
        }

        if (!package.IsActive)
        {
            package.IsActive = true;
            changed = true;
        }

        if (package.DisplayOrder != seed.CoinPackageDisplayOrder)
        {
            package.DisplayOrder = seed.CoinPackageDisplayOrder;
            changed = true;
        }

        if (changed)
        {
            package.UpdatedAt = updatedAt;
        }

        return changed;
    }

    private static bool MatchesName(
        string existingName,
        string canonicalName,
        IReadOnlyList<string> legacyNames)
    {
        if (string.Equals(existingName, canonicalName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return legacyNames.Any(legacyName =>
            string.Equals(existingName, legacyName, StringComparison.OrdinalIgnoreCase));
    }
}
