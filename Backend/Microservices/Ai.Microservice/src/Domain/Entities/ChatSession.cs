namespace Domain.Entities;

public sealed class ChatSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? SessionName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public ICollection<Chat> Chats { get; set; } = new List<Chat>();
}
