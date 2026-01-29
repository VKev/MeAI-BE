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

        var seeds = new[]
        {
            new { Name = "Subscription 10000", Coin = 10000m, Limit = 3 },
            new { Name = "Subscription 15000", Coin = 15000m, Limit = 5 },
            new { Name = "Subscription 20000", Coin = 20000m, Limit = 10 }
        };

        var toAdd = new List<Subscription>();
        foreach (var seed in seeds)
        {
            if (existingNameSet.Contains(seed.Name))
            {
                continue;
            }

            toAdd.Add(new Subscription
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                Cost = (float)(seed.Coin * 10m),
                DurationMonths = 1,
                MeAiCoin = seed.Coin,
                Limits = new SubscriptionLimits
                {
                    NumberOfSocialAccounts = seed.Limit,
                    RateLimitForContentCreation = seed.Limit,
                    NumberOfWorkspaces = seed.Limit
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

