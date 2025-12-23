namespace Domain.Entities;

public sealed class Chat
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public string? Prompt { get; set; }

    public string? Config { get; set; }

    public string? ReferenceResourceIds { get; set; }

    public string? ResultResourceIds { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public ChatSession Session { get; set; } = null!;
}
