using Domain.Entities;

namespace Domain.Repositories;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken);
    Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken);
}
