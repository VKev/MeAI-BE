using Application.Notifications.Models;

namespace Application.Abstractions.Notifications;

public interface INotificationRealtimeNotifier
{
    Task NotifyUserAsync(NotificationDeliveryModel notification, CancellationToken cancellationToken);
}
