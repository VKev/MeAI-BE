namespace SharedLibrary.Contracts.Notifications;

public class NotificationRequestedEvent
{
    public Guid NotificationId { get; set; }

    public string Source { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? PayloadJson { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Guid> RecipientUserIds { get; set; } = [];
}
