using Application.Abstractions.Notifications;
using StackExchange.Redis;

namespace Infrastructure.Logic.Services;

public sealed class RedisNotificationPresenceService : INotificationPresenceService
{
    private static readonly TimeSpan PresenceExpiry = TimeSpan.FromHours(6);
    private readonly IDatabase _database;

    public RedisNotificationPresenceService(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public async Task RegisterConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        var userKey = GetUserPresenceKey(userId);
        var connectionKey = GetConnectionPresenceKey(connectionId);

        await _database.SetAddAsync(userKey, connectionId);
        await _database.KeyExpireAsync(userKey, PresenceExpiry);
        await _database.StringSetAsync(connectionKey, userId.ToString(), PresenceExpiry);
    }

    public async Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken)
    {
        var connectionKey = GetConnectionPresenceKey(connectionId);
        var userIdValue = await _database.StringGetAsync(connectionKey);
        if (!userIdValue.HasValue || !Guid.TryParse(userIdValue.ToString(), out var userId))
        {
            await _database.KeyDeleteAsync(connectionKey);
            return;
        }

        var userKey = GetUserPresenceKey(userId);
        await _database.SetRemoveAsync(userKey, connectionId);
        await _database.KeyDeleteAsync(connectionKey);

        if (await _database.SetLengthAsync(userKey) == 0)
        {
            await _database.KeyDeleteAsync(userKey);
        }
    }

    public async Task<bool> IsUserOnlineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userKey = GetUserPresenceKey(userId);
        return await _database.KeyExistsAsync(userKey) && await _database.SetLengthAsync(userKey) > 0;
    }

    private static string GetUserPresenceKey(Guid userId) => $"notifications:presence:user:{userId:N}";

    private static string GetConnectionPresenceKey(string connectionId) => $"notifications:presence:connection:{connectionId}";
}
