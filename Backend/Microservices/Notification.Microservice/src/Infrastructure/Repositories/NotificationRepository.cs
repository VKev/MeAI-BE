using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly DbSet<Notification> _dbSet;

    public NotificationRepository(MyDbContext dbContext)
    {
        _dbSet = dbContext.Set<Notification>();
    }

    public Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(notification, cancellationToken).AsTask();
    }

    public Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        return _dbSet.AsNoTracking()
            .Include(notification => notification.UserNotifications)
            .FirstOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);
    }
}
