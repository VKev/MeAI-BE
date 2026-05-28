using System.Text.Json;
using Application.GenerationOptions;
using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Seeding;

public sealed class GenerationOptionsSeeder
{
    private static readonly SocialSeed[] SocialDefaults =
    [
        new("image", "facebook", "Facebook", "post", "Post", ["1:1", "16:9"], "1:1", 10),
        new("image", "facebook", "Facebook", "reel", "Reel", ["9:16"], "9:16", 20),
        new("image", "instagram", "Instagram", "post", "Post", ["1:1", "4:5"], "1:1", 30),
        new("image", "instagram", "Instagram", "reel", "Reel", ["9:16"], "9:16", 40),
        new("image", "tiktok", "TikTok", "reel", "Reel", ["9:16"], "9:16", 50),
        new("image", "threads", "Threads", "post", "Post", ["1:1", "16:9"], "1:1", 60),
        new("video", "tiktok", "TikTok", "reel", "Reel", ["9:16"], "9:16", 10),
        new("video", "facebook", "Facebook", "post", "Post", ["16:9"], "16:9", 20),
        new("video", "instagram", "Instagram", "reel", "Reel", ["9:16"], "9:16", 30),
        new("video", "threads", "Threads", "post", "Post", ["9:16"], "9:16", 40)
    ];

    private readonly MyDbContext _dbContext;
    private readonly ILogger<GenerationOptionsSeeder> _logger;

    public GenerationOptionsSeeder(MyDbContext dbContext, ILogger<GenerationOptionsSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingModels = await _dbContext.GenerationModelOptions
            .ToListAsync(cancellationToken);
        var existingPresets = await _dbContext.GenerationSocialPresets
            .ToListAsync(cancellationToken);

        var modelAdds = ProviderGenerationModelCatalog.DefaultSeedModels
            .Where(seed => existingModels.All(item =>
                !string.Equals(item.Mode, seed.Mode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.ModelId, seed.ModelId, StringComparison.OrdinalIgnoreCase)))
            .Select(ToEntity)
            .ToList();

        var presetAdds = SocialDefaults
            .Where(seed => existingPresets.All(item =>
                !string.Equals(item.Mode, seed.Mode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.Platform, seed.Platform, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.ContentType, seed.ContentType, StringComparison.OrdinalIgnoreCase)))
            .Select(ToEntity)
            .ToList();

        if (modelAdds.Count == 0 && presetAdds.Count == 0)
        {
            _logger.LogInformation("Generation options catalog already contains seed data.");
            return;
        }

        if (modelAdds.Count > 0)
        {
            _dbContext.GenerationModelOptions.AddRange(modelAdds);
        }

        if (presetAdds.Count > 0)
        {
            _dbContext.GenerationSocialPresets.AddRange(presetAdds);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded {ModelCount} generation model options and {PresetCount} generation social presets.",
            modelAdds.Count,
            presetAdds.Count);
    }

    private static GenerationModelOption ToEntity(ProviderGenerationModelOption seed)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        return new GenerationModelOption
        {
            Id = Guid.CreateVersion7(),
            Mode = seed.Mode,
            ModelId = seed.ModelId,
            Name = seed.Name,
            Description = seed.Description,
            SupportedRatiosJson = JsonSerializer.Serialize(seed.SupportedRatios),
            SupportedQualitiesJson = JsonSerializer.Serialize(seed.SupportedQualities),
            SupportsResolution = seed.SupportsResolution,
            IsActive = true,
            SortOrder = seed.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static GenerationSocialPreset ToEntity(SocialSeed seed)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        return new GenerationSocialPreset
        {
            Id = Guid.CreateVersion7(),
            Mode = seed.Mode,
            Platform = seed.Platform,
            Label = seed.Label,
            ContentType = seed.ContentType,
            ContentLabel = seed.ContentLabel,
            SupportedRatiosJson = JsonSerializer.Serialize(seed.SupportedRatios),
            DefaultRatio = seed.DefaultRatio,
            IsActive = true,
            SortOrder = seed.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed record SocialSeed(
        string Mode,
        string Platform,
        string Label,
        string ContentType,
        string ContentLabel,
        IReadOnlyList<string> SupportedRatios,
        string DefaultRatio,
        int SortOrder);
}
