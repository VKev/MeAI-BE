namespace Application.Notifications.Models;

public sealed record NotificationDeliveryModel(
    Guid NotificationId,
    Guid UserNotificationId,
    Guid UserId,
    string Type,
    string Title,
    string Message,
    string? PayloadJson,
    Guid? CreatedByUserId,
    bool IsRead,
    DateTime? ReadAt,
    bool WasOnlineWhenCreated,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
