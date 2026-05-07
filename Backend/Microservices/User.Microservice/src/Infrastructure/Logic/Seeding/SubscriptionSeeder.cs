using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Seeding;

public sealed class SubscriptionSeeder
{
    private readonly MyDbContext _dbContext;
    private readonly ILogger<SubscriptionSeeder> _logger;

    public SubscriptionSeeder(MyDbContext dbContext, ILogger<SubscriptionSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingNames = await _dbContext.Subscriptions
            .AsNoTracking()
            .Select(subscription => subscription.Name)
            .Where(name => name != null)
            .ToListAsync(cancellationToken);

        var existingNameSet = new HashSet<string>(existingNames!, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        var toAdd = new List<Subscription>();
        foreach (var seed in BillingSeedCatalog.Tiers)
        {
            if (existingNameSet.Contains(seed.SubscriptionName))
            {
                continue;
            }

            toAdd.Add(new Subscription
            {
                Id = Guid.NewGuid(),
                Name = seed.SubscriptionName,
                Cost = (float)seed.SubscriptionCostVnd,
                DurationMonths = 1,
                MeAiCoin = seed.CoinAmount,
                Limits = new SubscriptionLimits
                {
                    NumberOfSocialAccounts = seed.SocialAccounts,
                    RateLimitForContentCreation = seed.ContentRate,
                    NumberOfWorkspaces = null,
                    MaxPagesPerSocialAccount = seed.MaxPages,
                    StorageQuotaBytes = 10L * 1024L * 1024L * 1024L,
                    MaxUploadFileBytes = 500L * 1024L * 1024L,
                    RetentionDaysAfterDelete = 30
                },
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (toAdd.Count == 0)
        {
            _logger.LogInformation("Subscription seed skipped; subscriptions already exist.");
            return;
        }

        _dbContext.Subscriptions.AddRange(toAdd);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} subscription(s).", toAdd.Count);
    }
}
