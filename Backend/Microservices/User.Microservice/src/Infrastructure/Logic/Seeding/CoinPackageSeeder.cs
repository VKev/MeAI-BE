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
        var existingNames = await _dbContext.CoinPackages
            .AsNoTracking()
            .Select(package => package.Name)
            .Where(name => name != null)
            .ToListAsync(cancellationToken);

        var existingNameSet = new HashSet<string>(existingNames!, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var toAdd = new List<CoinPackage>();

        foreach (var seed in BillingSeedCatalog.Tiers)
        {
            if (existingNameSet.Contains(seed.CoinPackageName))
            {
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

        if (toAdd.Count == 0)
        {
            _logger.LogInformation("Coin package seed skipped; coin packages already exist.");
            return;
        }

        _dbContext.CoinPackages.AddRange(toAdd);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} coin package(s).", toAdd.Count);
    }

    private static string ResolveCurrency(BillingCurrencyOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Currency)
            ? "vnd"
            : options.Currency.Trim().ToLowerInvariant();
    }
}
