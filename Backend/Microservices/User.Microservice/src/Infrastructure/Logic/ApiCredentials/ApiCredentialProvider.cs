using Application.Abstractions.ApiCredentials;
using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace Infrastructure.Logic.ApiCredentials;

public sealed class ApiCredentialProvider : IApiCredentialProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ApiCredentialCryptoService _cryptoService;

    public ApiCredentialProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        ApiCredentialCryptoService cryptoService)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _configuration = configuration;
        _cryptoService = cryptoService;
    }

    public string GetRequiredValue(string provider, string keyName)
    {
        var value = GetOptionalValue(provider, keyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"{provider}:{keyName} is not configured.");
    }

    public string? GetOptionalValue(string provider, string keyName)
    {
        var cacheKey = BuildCacheKey(provider, keyName);
        if (_cache.TryGetValue<string?>(cacheKey, out var cached))
        {
            return cached;
        }

        var resolved = LoadFromDatabase(provider, keyName) ?? LoadFromConfiguration(provider, keyName);
        _cache.Set(cacheKey, resolved, CacheDuration);
        return resolved;
    }

    public void StoreValue(string provider, string keyName, string? value)
    {
        _cache.Set(BuildCacheKey(provider, keyName), value, CacheDuration);
    }

    public void Invalidate(string provider, string keyName)
    {
        _cache.Remove(BuildCacheKey(provider, keyName));
    }

    private string? LoadFromDatabase(string provider, string keyName)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MyDbContext>();

        var credential = context.ApiCredentials
            .AsNoTracking()
            .FirstOrDefault(item =>
                !item.IsDeleted &&
                item.IsActive &&
                item.ServiceName == ApiCredentialCatalog.ServiceName &&
                item.Provider == provider &&
                item.KeyName == keyName);

        if (credential is null || string.IsNullOrWhiteSpace(credential.ValueEncrypted))
        {
            return null;
        }

        try
        {
            return _cryptoService.Decrypt(credential.ValueEncrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private string? LoadFromConfiguration(string provider, string keyName)
    {
        var definition = ApiCredentialCatalog.Find(provider, keyName);
        if (definition is null)
        {
            return null;
        }

        foreach (var configKey in definition.ConfigurationKeys)
        {
            var value = _configuration[configKey];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string BuildCacheKey(string provider, string keyName)
    {
        return $"{ApiCredentialCatalog.ServiceName}:{provider.Trim().ToUpperInvariant()}:{keyName.Trim().ToUpperInvariant()}";
    }
}
