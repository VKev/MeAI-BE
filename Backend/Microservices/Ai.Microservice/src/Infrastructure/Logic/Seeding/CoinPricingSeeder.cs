using Application.Billing;
using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Seeding;

public sealed class CoinPricingSeeder
{
    // Billing baseline:
    //   - 1 coin ~= 1,000 VND
    //   - 1 USD ~= 26,309 VND (reference snapshot used for seed calibration)
    //   - therefore 1 USD ~= 26.31 coins
    //
    // Kie-backed image/video entries keep the existing product markup assumptions, but are
    // now converted into the real coin currency instead of the old "$0.01 per coin" scheme.
    //
    // OpenRouter-backed text/image workflows are seeded from current OpenRouter pricing or
    // from the repo's documented workflow cost (for composite aliases such as draft-post-v1).
    // Model="*" is a wildcard fallback — any unseeded model falls back to this row so a
    // brand-new Kie model doesn't 400 the generation request. Admins can tweak at runtime.
    private static readonly (string ActionType, string Model, string? Variant, string Unit, decimal Cost)[] Defaults =
    {
        (CoinActionTypes.ImageGeneration, "nano-banana-pro", "1K", "per_image", UsdToCoins(0.06m)),
        (CoinActionTypes.ImageGeneration, "nano-banana-pro", "2K", "per_image", UsdToCoins(0.12m)),
        (CoinActionTypes.ImageGeneration, "nano-banana-pro", null, "per_image", UsdToCoins(0.06m)),
        (CoinActionTypes.ImageGeneration, "ideogram/v3-text-to-image", "1K", "per_image", UsdToCoins(0.16m)),
        (CoinActionTypes.ImageGeneration, "ideogram/v3-text-to-image", "2K", "per_image", UsdToCoins(0.24m)),
        (CoinActionTypes.ImageGeneration, "ideogram/v3-text-to-image", null, "per_image", UsdToCoins(0.16m)),
        (CoinActionTypes.ImageGeneration, "*", null, "per_image", UsdToCoins(0.10m)),
        (CoinActionTypes.ImageReframeVariant, "nano-banana-pro", null, "per_variant", UsdToCoins(0.06m)),
        (CoinActionTypes.ImageReframeVariant, "*", null, "per_variant", UsdToCoins(0.10m)),
        (CoinActionTypes.VideoGeneration, "veo3_fast", null, "per_clip", UsdToCoins(0.90m)),
        (CoinActionTypes.VideoGeneration, "veo3", null, "per_clip", UsdToCoins(5.40m)),
        (CoinActionTypes.VideoGeneration, "veo3_quality", null, "per_clip", UsdToCoins(5.40m)),
        (CoinActionTypes.VideoGeneration, "*", null, "per_clip", UsdToCoins(1.20m)),
        // Caption generation is charged per generated social platform/post.
        // The interactive captions endpoint uses OpenRouter GPT-4o. The mini rows cover
        // lighter legacy caption flows that still resolve via model-default pricing.
        (CoinActionTypes.CaptionGeneration, "openai/gpt-4o", null, "per_platform", UsdToCoins(0.015m)),
        (CoinActionTypes.CaptionGeneration, "gpt-4o-mini", null, "per_platform", UsdToCoins(0.0008m)),
        (CoinActionTypes.CaptionGeneration, "gpt-5-2", null, "per_platform", UsdToCoins(0.0010m)),
        (CoinActionTypes.CaptionGeneration, "*", null, "per_platform", UsdToCoins(0.0010m)),
        // Enhance-existing-post v1 reuses the same caption engine + price model as batch
        // caption generation, but tracks spend separately so usage/billing can distinguish it.
        (CoinActionTypes.PostEnhancement, "gpt-4o-mini", null, "per_platform", UsdToCoins(0.0008m)),
        (CoinActionTypes.PostEnhancement, "gpt-5-2", null, "per_platform", UsdToCoins(0.0010m)),
        // Recommendation draft and improve flows use fixed product pricing in the start
        // command handlers: 20 coins per request. Keep the catalog rows aligned for
        // admin visibility.
        (CoinActionTypes.PostEnhancement, "openrouter/improve-post-v1", "caption", "per_request", GeneratedPostCoinCost.BaseCoins),
        (CoinActionTypes.PostEnhancement, "openrouter/improve-post-v1", "image", "per_request", GeneratedPostCoinCost.BaseCoins),
        (CoinActionTypes.PostEnhancement, "openrouter/improve-post-v1", "caption_image", "per_request", GeneratedPostCoinCost.BaseCoins),
        (CoinActionTypes.PostEnhancement, "*", null, "per_platform", UsdToCoins(0.0010m)),
        (CoinActionTypes.DraftPostGeneration, "openrouter/draft-post-v1", null, "per_request", GeneratedPostCoinCost.BaseCoins),
        (CoinActionTypes.DraftPostGeneration, "*", null, "per_request", GeneratedPostCoinCost.BaseCoins),
        (CoinActionTypes.FormulaGeneration, "gpt-4o-mini", null, "per_variant", UsdToCoins(0.0004m)),
        (CoinActionTypes.FormulaGeneration, "*", null, "per_variant", UsdToCoins(0.0004m))
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
            .ToListAsync(cancellationToken);

        var toInsert = new List<CoinPricingCatalogEntry>();
        var updatedCount = 0;
        foreach (var row in Defaults)
        {
            var current = existing.FirstOrDefault(entry =>
                string.Equals(entry.ActionType, row.ActionType, StringComparison.Ordinal) &&
                string.Equals(entry.Model, row.Model, StringComparison.Ordinal) &&
                string.Equals(entry.Variant, row.Variant, StringComparison.Ordinal));

            if (current is not null)
            {
                if (ApplySeededValues(current, row))
                {
                    updatedCount++;
                }

                continue;
            }

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

        if (toInsert.Count == 0 && updatedCount == 0)
        {
            _logger.LogInformation("Coin pricing catalog already matches seed data.");
            return;
        }

        if (toInsert.Count > 0)
        {
            _dbContext.CoinPricingCatalog.AddRange(toInsert);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {AddedCount} coin pricing entries, updated {UpdatedCount} entries.",
            toInsert.Count,
            updatedCount);
    }

    private static bool ApplySeededValues(
        CoinPricingCatalogEntry entry,
        (string ActionType, string Model, string? Variant, string Unit, decimal Cost) row)
    {
        var changed = false;

        if (!string.Equals(entry.Unit, row.Unit, StringComparison.Ordinal))
        {
            entry.Unit = row.Unit;
            changed = true;
        }

        if (entry.UnitCostCoins != row.Cost)
        {
            entry.UnitCostCoins = row.Cost;
            changed = true;
        }

        if (!entry.IsActive)
        {
            entry.IsActive = true;
            changed = true;
        }

        if (changed)
        {
            entry.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        }

        return changed;
    }

    private static decimal UsdToCoins(decimal usd)
    {
        const decimal vndPerUsd = 26309m;
        const decimal vndPerCoin = 1000m;
        return Math.Round(usd * (vndPerUsd / vndPerCoin), 2, MidpointRounding.AwayFromZero);
    }
}
