using Application.Billing;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Logic.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiMicroservice.Tests.Infrastructure.Logic.Seeding;

public sealed class VideoGenerationSeedTests
{
    [Fact]
    public async Task GenerationOptionsSeeder_ShouldReplaceLegacyVideoSeedsAndPreserveCustomVideoModels()
    {
        await using var dbContext = CreateDbContext();
        dbContext.GenerationModelOptions.AddRange(
            CreateModel("veo3_fast"),
            CreateModel("veo3"),
            CreateModel("veo3_lite"),
            CreateModel("custom-video"));
        await dbContext.SaveChangesAsync();

        var seeder = new GenerationOptionsSeeder(
            dbContext,
            NullLogger<GenerationOptionsSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var activeVideoModels = await dbContext.GenerationModelOptions
            .Where(option => option.Mode == "video" && option.IsActive && option.DeletedAt == null)
            .Select(option => option.ModelId)
            .ToListAsync();

        activeVideoModels.Should().BeEquivalentTo(
            "custom-video",
            "gemini-omni-video",
            "grok-imagine-video-1-5-preview",
            "veo-3-1",
            "bytedance/seedance-2");

        var retiredVideoModels = await dbContext.GenerationModelOptions
            .Where(option => option.Mode == "video" && option.DeletedAt != null)
            .ToListAsync();

        retiredVideoModels.Select(option => option.ModelId).Should().BeEquivalentTo("veo3_fast", "veo3", "veo3_lite");
        retiredVideoModels.Should().OnlyContain(option => !option.IsActive);
    }

    [Fact]
    public async Task CoinPricingSeeder_ShouldSeedDefaultVideoPrices()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new CoinPricingSeeder(
            dbContext,
            NullLogger<CoinPricingSeeder>.Instance);

        await seeder.SeedAsync();

        var videoPrices = await dbContext.CoinPricingCatalog
            .Where(entry => entry.ActionType == CoinActionTypes.VideoGeneration
                && (entry.Model == "gemini-omni-video"
                    || entry.Model == "grok-imagine-video-1-5-preview"
                    || entry.Model == "veo-3-1"
                    || entry.Model == "bytedance/seedance-2"))
            .ToDictionaryAsync(
                entry => $"{entry.Model}:{entry.Variant ?? "default"}",
                entry => entry.UnitCostCoins);

        var expectedPrices = new Dictionary<string, decimal>
        {
            ["gemini-omni-video:default"] = 11.84m,
            ["gemini-omni-video:720p:4s"] = 11.84m,
            ["gemini-omni-video:720p:6s"] = 15.79m,
            ["gemini-omni-video:720p:8s"] = 19.73m,
            ["gemini-omni-video:720p:10s"] = 23.68m,
            ["gemini-omni-video:1080p:4s"] = 11.84m,
            ["gemini-omni-video:1080p:6s"] = 15.79m,
            ["gemini-omni-video:1080p:8s"] = 19.73m,
            ["gemini-omni-video:1080p:10s"] = 23.68m,
            ["gemini-omni-video:4k:4s"] = 27.62m,
            ["gemini-omni-video:4k:6s"] = 31.57m,
            ["gemini-omni-video:4k:8s"] = 35.52m,
            ["gemini-omni-video:4k:10s"] = 39.46m,
            ["grok-imagine-video-1-5-preview:default"] = 16.84m,
            ["grok-imagine-video-1-5-preview:480p"] = 2.10m,
            ["grok-imagine-video-1-5-preview:720p"] = 3.68m,
            ["veo-3-1:default"] = 7.89m,
            ["veo-3-1:lite"] = 3.95m,
            ["veo-3-1:fast"] = 7.89m,
            ["veo-3-1:quality"] = 32.89m,
            ["bytedance/seedance-2:default"] = 26.97m,
            ["bytedance/seedance-2:480p"] = 2.50m,
            ["bytedance/seedance-2:720p"] = 5.39m,
            ["bytedance/seedance-2:1080p"] = 13.42m
        };

        foreach (var (resolution, outputUsdPerSecond) in new[] { ("480p", 0.08m), ("720p", 0.14m) })
        {
            for (var duration = 1; duration <= 15; duration++)
            {
                expectedPrices[$"grok-imagine-video-1-5-preview:{resolution}:{duration}s"] =
                    UsdToCoins((outputUsdPerSecond * duration) + 0.01m);
            }
        }

        videoPrices.Should().BeEquivalentTo(expectedPrices);
    }

    [Theory]
    [InlineData("grok-imagine-video-1-5-preview", null, null, "480p:8s", 1)]
    [InlineData("gemini-omni-video", null, null, "720p:4s", 1)]
    [InlineData("gemini-omni-video", "4k", 10, "4k:10s", 1)]
    [InlineData("gemini-omni-video", "unsupported", 7, "720p:4s", 1)]
    [InlineData("grok-imagine-video-1-5-preview", "720p", 12, "720p:12s", 1)]
    [InlineData("bytedance/seedance-2", null, null, "720p", 5)]
    [InlineData("bytedance/seedance-2", "1080p", 15, "1080p", 15)]
    [InlineData("bytedance/seedance-2", "unsupported", 99, "720p", 15)]
    public void VideoPricingResolver_ShouldUseResolutionAndDurationForVariableRateModels(
        string model,
        string? resolution,
        int? duration,
        string expectedVariant,
        int expectedQuantity)
    {
        var pricing = VideoPricingResolver.Resolve(model, null, resolution, duration);

        pricing.CatalogVariant.Should().Be(expectedVariant);
        pricing.Quantity.Should().Be(expectedQuantity);
    }

    [Theory]
    [InlineData("gemini-omni-video", 6, 6)]
    [InlineData("gemini-omni-video", 7, 4)]
    [InlineData("grok-imagine-video-1-5-preview", null, 8)]
    [InlineData("grok-imagine-video-1-5-preview", 99, 15)]
    [InlineData("bytedance/seedance-2", 1, 4)]
    [InlineData("bytedance/seedance-2", 12, 12)]
    [InlineData("veo-3-1", 8, null)]
    public void VideoGenerationSettings_ShouldNormalizeProviderDuration(
        string model,
        int? duration,
        int? expectedDuration)
    {
        VideoGenerationSettings.NormalizeDuration(model, duration)
            .Should().Be(expectedDuration);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MyDbContext(options);
    }

    private static decimal UsdToCoins(decimal usd)
    {
        return Math.Round(usd * 26.309m, 2, MidpointRounding.AwayFromZero);
    }

    private static GenerationModelOption CreateModel(string modelId)
    {
        return new GenerationModelOption
        {
            Id = Guid.NewGuid(),
            Mode = "video",
            ModelId = modelId,
            Name = modelId,
            SupportedRatiosJson = """["16:9"]""",
            SupportedQualitiesJson = "[]",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
