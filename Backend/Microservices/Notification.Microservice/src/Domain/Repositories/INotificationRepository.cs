using Domain.Entities;

namespace Domain.Repositories;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid notificationId, CancellationToken cancellationToken);
}
