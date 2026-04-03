using Domain.Entities;
using Infrastructure.Context;
using Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Seeding;

public sealed class ConfigSeeder
{
    private readonly MyDbContext _dbContext;
    private readonly ConfigSeedOptions _options;
    private readonly ILogger<ConfigSeeder> _logger;

    public ConfigSeeder(
        MyDbContext dbContext,
        IOptions<ConfigSeedOptions> options,
        ILogger<ConfigSeeder> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingConfig = await _dbContext.Configs
            .AsNoTracking()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingConfig is not null)
        {
            _logger.LogInformation("Config seed skipped: active config already exists.");
            return;
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var config = new Config
        {
            Id = Guid.CreateVersion7(),
            ChatModel = Normalize(_options.ChatModel),
            MediaAspectRatio = Normalize(_options.MediaAspectRatio) ?? "1:1",
            NumberOfVariances = _options.NumberOfVariances,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _dbContext.Configs.AddAsync(config, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded config {ConfigId} with ChatModel={ChatModel}, MediaAspectRatio={MediaAspectRatio}, NumberOfVariances={NumberOfVariances}.",
            config.Id,
            config.ChatModel ?? "<null>",
            config.MediaAspectRatio ?? "<null>",
            config.NumberOfVariances?.ToString() ?? "<null>");
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
