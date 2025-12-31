using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Security;
using Application.Users.Helpers;
using StackExchange.Redis;

namespace Infrastructure.Repositories;

public sealed class RedisVerificationCodeStore(IConnectionMultiplexer multiplexer) : IVerificationCodeStore
{
    private readonly IDatabase _database = multiplexer.GetDatabase();

    public async Task StoreAsync(string purpose, string email, string code, TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(purpose, email);
        var hash = VerificationCodeGenerator.HashCode(code);
        await _database.StringSetAsync(key, hash, ttl);
    }

    public async Task<bool> ValidateAsync(string purpose, string email, string code,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(purpose, email);
        var storedHash = await _database.StringGetAsync(key);
        if (!storedHash.HasValue)
        {
            return false;
        }

        var candidateHash = VerificationCodeGenerator.HashCode(code);
        return FixedTimeEquals(storedHash.ToString(), candidateHash);
    }

    public async Task RemoveAsync(string purpose, string email, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(purpose, email);
        await _database.KeyDeleteAsync(key);
    }

    private static string BuildKey(string purpose, string email) =>
        $"verification:{purpose}:{email.Trim().ToLowerInvariant()}";

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
