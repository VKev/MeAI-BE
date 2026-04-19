using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class UserNotificationRepository : IUserNotificationRepository
{
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

        return await query
            .OrderByDescending(userNotification => userNotification.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
