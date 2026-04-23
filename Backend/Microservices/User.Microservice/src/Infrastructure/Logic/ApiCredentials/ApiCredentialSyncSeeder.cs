using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.ApiCredentials;

public sealed class ApiCredentialSyncSeeder
{
    private readonly MyDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ApiCredentialCryptoService _cryptoService;
    private readonly ILogger<ApiCredentialSyncSeeder> _logger;

    public ApiCredentialSyncSeeder(
        MyDbContext dbContext,
        IConfiguration configuration,
        ApiCredentialCryptoService cryptoService,
        ILogger<ApiCredentialSyncSeeder> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _cryptoService = cryptoService;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var existing = await _dbContext.ApiCredentials
            .Where(item => !item.IsDeleted && item.ServiceName == ApiCredentialCatalog.ServiceName)
            .ToListAsync(cancellationToken);

        var hasChanges = false;

        foreach (var definition in ApiCredentialCatalog.Definitions)
        {
            var configuredValue = ResolveConfigurationValue(definition);
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                continue;
            }

            var credential = existing.FirstOrDefault(item =>
                item.Provider == definition.Provider &&
                item.KeyName == definition.KeyName);

            var encryptedValue = _cryptoService.Encrypt(configuredValue);
            var last4 = GetLast4(configuredValue);

            if (credential is null)
            {
                _dbContext.ApiCredentials.Add(new ApiCredential
                {
                    Id = Guid.CreateVersion7(),
                    ServiceName = ApiCredentialCatalog.ServiceName,
                    Provider = definition.Provider,
                    KeyName = definition.KeyName,
                    DisplayName = definition.DisplayName,
                    ValueEncrypted = encryptedValue,
                    ValueLast4 = last4,
                    IsActive = true,
                    Source = "env_seeded",
                    Version = 1,
                    LastSyncedFromEnvAt = now,
                    LastRotatedAt = now,
                    CreatedAt = now
                });
                hasChanges = true;
                continue;
            }

            if (!string.Equals(credential.ValueEncrypted, encryptedValue, StringComparison.Ordinal))
            {
                credential.ValueEncrypted = encryptedValue;
                credential.ValueLast4 = last4;
                credential.Version = credential.Version <= 0 ? 1 : credential.Version + 1;
                credential.LastRotatedAt = now;
                hasChanges = true;
            }

            if (!string.Equals(credential.DisplayName, definition.DisplayName, StringComparison.Ordinal))
            {
                credential.DisplayName = definition.DisplayName;
                hasChanges = true;
            }

            credential.IsActive = true;
            credential.Source = "env_seeded";
            credential.LastSyncedFromEnvAt = now;
            credential.UpdatedAt = now;
        }

        if (!hasChanges)
        {
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Synced API credentials from env for {ServiceName}.", ApiCredentialCatalog.ServiceName);
    }

    private string? ResolveConfigurationValue(ApiCredentialDefinition definition)
    {
        foreach (var configKey in definition.ConfigurationKeys)
        {
            var value = _configuration[configKey];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? GetLast4(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 4 ? value : value[^4..];
    }
}
