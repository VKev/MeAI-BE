using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Infrastructure.Repositories;

public sealed class UserNotificationRepository : IUserNotificationRepository
{
    private const int RelatedIdBatchSize = 80;
    private const int RelatedIdMaxScannedRows = 5000;

    private readonly DbSet<UserNotification> _dbSet;

    public UserNotificationRepository(MyDbContext dbContext)
    {
        _dbSet = dbContext.Set<UserNotification>();
    }

    public Task AddAsync(UserNotification userNotification, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(userNotification, cancellationToken).AsTask();
    }

    public Task AddRangeAsync(IEnumerable<UserNotification> userNotifications, CancellationToken cancellationToken)
    {
        return _dbSet.AddRangeAsync(userNotifications, cancellationToken);
    }

    public Task<UserNotification?> GetByNotificationIdAndUserIdAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return _dbSet.AsNoTracking()
            .Include(userNotification => userNotification.Notification)
            .FirstOrDefaultAsync(
                userNotification => userNotification.NotificationId == notificationId && userNotification.UserId == userId,
                cancellationToken);
    }

    public Task<UserNotification?> GetTrackedByIdAndUserIdAsync(
        Guid userNotificationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return _dbSet
            .Include(userNotification => userNotification.Notification)
            .FirstOrDefaultAsync(
                userNotification => userNotification.Id == userNotificationId && userNotification.UserId == userId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<UserNotification>> GetUnreadTrackedByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(userNotification => userNotification.UserId == userId && !userNotification.IsRead)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserNotification>> GetByUserIdAsync(
        Guid userId,
        bool onlyUnread,
        int limit,
        string? source,
        string? typePrefix,
        string? relatedId,
        DateTime? beforeCreatedAt,
        CancellationToken cancellationToken)
    {
        var query = _dbSet.AsNoTracking()
            .Include(userNotification => userNotification.Notification)
            .Where(userNotification => userNotification.UserId == userId);

        if (onlyUnread)
        {
            query = query.Where(userNotification => !userNotification.IsRead);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(userNotification => userNotification.Notification.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(typePrefix))
        {
            query = query.Where(userNotification => userNotification.Notification.Type.StartsWith(typePrefix));
        }

        if (beforeCreatedAt.HasValue)
        {
            query = query.Where(userNotification => userNotification.CreatedAt < beforeCreatedAt.Value);
        }

        if (!string.IsNullOrWhiteSpace(relatedId))
        {
            return await GetByRelatedIdAsync(query, relatedId.Trim(), limit, cancellationToken);
        }

        return await query
            .OrderByDescending(userNotification => userNotification.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<UserNotification>> GetByRelatedIdAsync(
        IQueryable<UserNotification> baseQuery,
        string relatedId,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = new List<UserNotification>(limit);
        DateTime? cursor = null;
        var scannedRows = 0;

        while (results.Count < limit && scannedRows < RelatedIdMaxScannedRows)
        {
            var pageQuery = baseQuery;
            if (cursor.HasValue)
            {
                pageQuery = pageQuery.Where(userNotification => userNotification.CreatedAt < cursor.Value);
            }

            var batch = await pageQuery
                .OrderByDescending(userNotification => userNotification.CreatedAt)
                .Take(RelatedIdBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var userNotification in batch)
            {
                if (PayloadMatchesRelatedId(userNotification.Notification.PayloadJson, relatedId))
                {
                    results.Add(userNotification);
                    if (results.Count >= limit)
                    {
                        break;
                    }
                }
            }

            scannedRows += batch.Count;
            cursor = batch[^1].CreatedAt;

            if (batch.Count < RelatedIdBatchSize)
            {
                break;
            }
        }

        return results;
    }

    private static bool PayloadMatchesRelatedId(string? payloadJson, string relatedId)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);
            var root = payload.RootElement;
            return MatchesStringProperty(root, "correlationId", relatedId)
                   || MatchesStringProperty(root, "postId", relatedId)
                   || MatchesStringProperty(root, "draftPostId", relatedId);
        }
        catch (JsonException)
        {
            return payloadJson.Contains(relatedId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool MatchesStringProperty(JsonElement root, string propertyName, string expected)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
