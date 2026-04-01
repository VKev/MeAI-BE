namespace Application.Notifications.Models;

public sealed record MarkAllNotificationsAsReadResponse(int UpdatedCount, DateTime MarkedAt);
