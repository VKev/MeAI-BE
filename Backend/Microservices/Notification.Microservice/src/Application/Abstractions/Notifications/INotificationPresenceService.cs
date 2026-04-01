namespace Application.Abstractions.Notifications;

public interface INotificationPresenceService
{
    Task RegisterConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken);
    Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken);
    Task<bool> IsUserOnlineAsync(Guid userId, CancellationToken cancellationToken);
}
