using System.Security.Claims;
using Application.Abstractions.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace WebApi.Hubs;

public sealed class NotificationHub : Hub
{
    public const string HubRoute = "/hubs/notifications";
    public const string NotificationReceivedMethod = "NotificationReceived";

    private readonly INotificationPresenceService _notificationPresenceService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        INotificationPresenceService notificationPresenceService,
        ILogger<NotificationHub> logger)
    {
        _notificationPresenceService = notificationPresenceService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryGetUserId(out var userId))
        {
            _logger.LogWarning("Notification hub connection rejected because user id claim is missing.");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroup(userId));
        await _notificationPresenceService.RegisterConnectionAsync(userId, Context.ConnectionId, Context.ConnectionAborted);

        _logger.LogInformation(
            "Notification hub connected. UserId: {UserId}, ConnectionId: {ConnectionId}",
            userId,
            Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetUserGroup(userId));
        }

        await _notificationPresenceService.UnregisterConnectionAsync(Context.ConnectionId, CancellationToken.None);

        _logger.LogInformation(
            "Notification hub disconnected. ConnectionId: {ConnectionId}",
            Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetUserGroup(Guid userId) => $"notifications:user:{userId:N}";

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}
