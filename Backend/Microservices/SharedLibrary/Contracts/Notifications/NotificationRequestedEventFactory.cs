using System.Text.Json;

namespace SharedLibrary.Contracts.Notifications;

public static class NotificationRequestedEventFactory
{
    public static NotificationRequestedEvent CreateForUser(
        Guid userId,
        string type,
        string title,
        string message,
        object? payload = null,
        Guid? createdByUserId = null,
        DateTime? createdAt = null,
        string source = NotificationSourceConstants.Creator)
    {
        return new NotificationRequestedEvent
        {
            NotificationId = Guid.CreateVersion7(),
            Source = source,
            Type = type,
            Title = title,
            Message = message,
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload),
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            RecipientUserIds = [userId]
        };
    }
}
