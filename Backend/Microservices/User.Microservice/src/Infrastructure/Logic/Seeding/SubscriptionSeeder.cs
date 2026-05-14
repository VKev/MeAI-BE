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
        var existingSubscriptions = await _dbContext.Subscriptions
            .Where(subscription => subscription.Name != null)
            .ToListAsync(cancellationToken);

        var existingByName = existingSubscriptions
            .ToDictionary(subscription => subscription.Name!, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        var toAdd = new List<Subscription>();
        var updatedCount = 0;
        foreach (var seed in BillingSeedCatalog.Tiers)
        {
            if (existingByName.TryGetValue(seed.SubscriptionName, out var existing))
            {
                if (ApplySeededLimits(existing, seed, now))
                {
                    updatedCount++;
                }

                continue;
            }

            toAdd.Add(new Subscription
            {
                Id = Guid.NewGuid(),
                Name = seed.SubscriptionName,
                Cost = (float)seed.SubscriptionCostVnd,
                DurationMonths = 1,
                MeAiCoin = seed.CoinAmount,
                Limits = CreateSeededLimits(seed),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (toAdd.Count == 0 && updatedCount == 0)
        {
            _logger.LogInformation("Subscription seed skipped; subscriptions already match seed data.");
            return;
        }

        if (toAdd.Count > 0)
        {
            _dbContext.Subscriptions.AddRange(toAdd);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {AddedCount} subscription(s), updated {UpdatedCount} subscription(s).",
            toAdd.Count,
            updatedCount);
    }

    private static SubscriptionLimits CreateSeededLimits(BillingSeedTier seed)
    {
        return new SubscriptionLimits
        {
            NumberOfSocialAccounts = seed.SocialAccounts,
            RateLimitForContentCreation = seed.ContentRate,
            NumberOfWorkspaces = seed.Workspaces,
            MaxPagesPerSocialAccount = seed.MaxPages,
            StorageQuotaBytes = 10L * 1024L * 1024L * 1024L,
            MaxUploadFileBytes = 500L * 1024L * 1024L,
            RetentionDaysAfterDelete = 30
        };
    }

    private static bool ApplySeededLimits(Subscription subscription, BillingSeedTier seed, DateTime updatedAt)
    {
        subscription.Limits ??= new SubscriptionLimits();

        if (subscription.Limits.NumberOfWorkspaces == seed.Workspaces)
        {
            return false;
        }

        subscription.Limits.NumberOfWorkspaces = seed.Workspaces;
        subscription.UpdatedAt = updatedAt;
        return true;
    }
}
