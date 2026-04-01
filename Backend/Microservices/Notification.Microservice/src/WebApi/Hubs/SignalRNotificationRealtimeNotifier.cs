using Application.Abstractions.Notifications;
using Application.Notifications.Models;
using Microsoft.AspNetCore.SignalR;

namespace WebApi.Hubs;

public sealed class SignalRNotificationRealtimeNotifier : INotificationRealtimeNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public SignalRNotificationRealtimeNotifier(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyUserAsync(NotificationDeliveryModel notification, CancellationToken cancellationToken)
    {
        return _hubContext.Clients
            .Group(NotificationHub.GetUserGroup(notification.UserId))
            .SendAsync(NotificationHub.NotificationReceivedMethod, notification, cancellationToken);
    }
}
