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

        var now = DateTime.UtcNow;

        var toAdd = new List<Subscription>();
        var updatedCount = 0;
        foreach (var seed in BillingSeedCatalog.Tiers)
        {
            var existing = FindExistingSubscription(existingSubscriptions, seed);
            if (existing is not null)
            {
                if (ApplySeededValues(existing, seed, now))
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
            StorageQuotaBytes = seed.StorageQuotaBytes,
            MaxUploadFileBytes = 500L * 1024L * 1024L,
            RetentionDaysAfterDelete = 30
        };
    }

    private static Subscription? FindExistingSubscription(
        IReadOnlyList<Subscription> existingSubscriptions,
        BillingSeedTier seed)
    {
        return existingSubscriptions.FirstOrDefault(subscription =>
            MatchesName(subscription.Name, seed.SubscriptionName, seed.LegacySubscriptionNames));
    }

    private static bool ApplySeededValues(Subscription subscription, BillingSeedTier seed, DateTime updatedAt)
    {
        var changed = false;

        if (!string.Equals(subscription.Name, seed.SubscriptionName, StringComparison.Ordinal))
        {
            subscription.Name = seed.SubscriptionName;
            changed = true;
        }

        if (subscription.Cost != (float)seed.SubscriptionCostVnd)
        {
            subscription.Cost = (float)seed.SubscriptionCostVnd;
            changed = true;
        }

        if (subscription.MeAiCoin != seed.CoinAmount)
        {
            subscription.MeAiCoin = seed.CoinAmount;
            changed = true;
        }

        if (subscription.DurationMonths != 1)
        {
            subscription.DurationMonths = 1;
            changed = true;
        }

        if (!subscription.IsActive)
        {
            subscription.IsActive = true;
            changed = true;
        }

        var seededLimits = CreateSeededLimits(seed);
        if (!LimitsEqual(subscription.Limits, seededLimits))
        {
            subscription.Limits = seededLimits;
            changed = true;
        }

        if (changed)
        {
            subscription.UpdatedAt = updatedAt;
        }

        return changed;
    }

    private static bool LimitsEqual(SubscriptionLimits? current, SubscriptionLimits seeded)
    {
        if (current is null)
        {
            return false;
        }

        return current.NumberOfSocialAccounts == seeded.NumberOfSocialAccounts
            && current.RateLimitForContentCreation == seeded.RateLimitForContentCreation
            && current.NumberOfWorkspaces == seeded.NumberOfWorkspaces
            && current.MaxPagesPerSocialAccount == seeded.MaxPagesPerSocialAccount
            && current.StorageQuotaBytes == seeded.StorageQuotaBytes
            && current.MaxUploadFileBytes == seeded.MaxUploadFileBytes
            && current.RetentionDaysAfterDelete == seeded.RetentionDaysAfterDelete;
    }

    private static bool MatchesName(
        string? existingName,
        string canonicalName,
        IReadOnlyList<string> legacyNames)
    {
        if (string.IsNullOrWhiteSpace(existingName))
        {
            return false;
        }

        if (string.Equals(existingName, canonicalName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return legacyNames.Any(legacyName =>
            string.Equals(existingName, legacyName, StringComparison.OrdinalIgnoreCase));
    }
}
