namespace Domain.Entities;

public sealed class Notification
{
    public Guid Id { get; set; }

    public string Source { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? PayloadJson { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public ICollection<UserNotification> UserNotifications { get; set; } = new List<UserNotification>();
}
