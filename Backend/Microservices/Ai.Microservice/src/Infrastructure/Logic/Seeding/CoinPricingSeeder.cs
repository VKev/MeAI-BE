using Application.Billing;
using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Seeding;

public sealed class CoinPricingSeeder
{
    // 1 coin = $0.01 USD, markup 2x over raw Kie cost:
    //   - nano-banana-pro image (1K)  ~$0.03 raw ×2 = $0.06 = 6 coins
    //   - nano-banana-pro image (2K)  ~$0.06 raw ×2 = $0.12 = 12 coins
    //   - ideogram/v3 image           ~$0.08 raw ×2 = $0.16 = 16 coins
    //   - image reframe variant       same as source gen at 1K = 6 coins
    //   - veo3_fast 8s clip           ~$0.45 raw ×2 = $0.90 = 90 coins
    //   - veo3 / veo3_quality 8s      ~$2.70 raw ×2 = $5.40 = 540 coins
    // Model="*" is a wildcard fallback — any unseeded model falls back to this row so a
    // brand-new Kie model doesn't 400 the generation request. Admins can tweak at runtime.
    private static readonly (string ActionType, string Model, string? Variant, string Unit, decimal Cost)[] Defaults =
    {
        (CoinActionTypes.ImageGeneration, "nano-banana-pro", "1K", "per_image", 6m),
        (CoinActionTypes.ImageGeneration, "nano-banana-pro", "2K", "per_image", 12m),
        (CoinActionTypes.ImageGeneration, "nano-banana-pro", null, "per_image", 6m),
        (CoinActionTypes.ImageGeneration, "ideogram/v3-text-to-image", "1K", "per_image", 16m),
        (CoinActionTypes.ImageGeneration, "ideogram/v3-text-to-image", "2K", "per_image", 24m),
        (CoinActionTypes.ImageGeneration, "ideogram/v3-text-to-image", null, "per_image", 16m),
        (CoinActionTypes.ImageGeneration, "*", null, "per_image", 10m),
        (CoinActionTypes.ImageReframeVariant, "nano-banana-pro", null, "per_variant", 6m),
        (CoinActionTypes.ImageReframeVariant, "*", null, "per_variant", 10m),
        (CoinActionTypes.VideoGeneration, "veo3_fast", null, "per_clip", 90m),
        (CoinActionTypes.VideoGeneration, "veo3", null, "per_clip", 540m),
        (CoinActionTypes.VideoGeneration, "veo3_quality", null, "per_clip", 540m),
        (CoinActionTypes.VideoGeneration, "*", null, "per_clip", 120m),
        // Caption generation via Kie's GPT-5.4 Responses API. Per-platform charge (one row
        // per social type selected in the Generate call). Price tuned to 2x raw Kie-credit
        // spend so refunds stay whole-number and we absorb small FX drift.
        (CoinActionTypes.CaptionGeneration, "gpt-5-4", null, "per_platform", 3m),
        (CoinActionTypes.CaptionGeneration, "gpt-5-2", null, "per_platform", 2m),
        (CoinActionTypes.CaptionGeneration, "*", null, "per_platform", 3m)
    };

    private readonly MyDbContext _dbContext;
    private readonly ILogger<CoinPricingSeeder> _logger;

    public CoinPricingSeeder(MyDbContext dbContext, ILogger<CoinPricingSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.CoinPricingCatalog
            .AsNoTracking()
            .Select(e => new { e.ActionType, e.Model, e.Variant })
            .ToListAsync(cancellationToken);

        var existingKeys = new HashSet<string>(
            existing.Select(e => Key(e.ActionType, e.Model, e.Variant)));

        var toInsert = new List<CoinPricingCatalogEntry>();
        foreach (var row in Defaults)
        {
            var key = Key(row.ActionType, row.Model, row.Variant);
            if (existingKeys.Contains(key)) continue;

            toInsert.Add(new CoinPricingCatalogEntry
            {
                Id = Guid.CreateVersion7(),
                ActionType = row.ActionType,
                Model = row.Model,
                Variant = row.Variant,
                Unit = row.Unit,
                UnitCostCoins = row.Cost,
                IsActive = true,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            });
        }

        if (toInsert.Count == 0)
        {
            _logger.LogInformation("Coin pricing catalog already seeded — nothing to add.");
            return;
        }

        _dbContext.CoinPricingCatalog.AddRange(toInsert);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} coin pricing entries.", toInsert.Count);
    }

    private static string Key(string actionType, string model, string? variant) =>
        $"{actionType}:{model}:{variant ?? ""}";
}
