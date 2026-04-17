using Domain.Entities;

namespace Domain.Repositories;

public interface IUserNotificationRepository
{
    Task AddAsync(UserNotification userNotification, CancellationToken cancellationToken);
    Task AddRangeAsync(IEnumerable<UserNotification> userNotifications, CancellationToken cancellationToken);
    Task<UserNotification?> GetByNotificationIdAndUserIdAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken);
    Task<UserNotification?> GetTrackedByIdAndUserIdAsync(Guid userNotificationId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserNotification>> GetUnreadTrackedByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserNotification>> GetByUserIdAsync(
        Guid userId,
        bool onlyUnread,
        int limit,
        string? source,
        CancellationToken cancellationToken);
}
