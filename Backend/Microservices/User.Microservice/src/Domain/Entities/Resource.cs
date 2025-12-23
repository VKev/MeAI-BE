namespace Domain.Entities;

public sealed class Resource
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Link { get; set; } = null!;

    public string? Status { get; set; }

    public string? ResourceType { get; set; }

    public string? ContentType { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public User User { get; set; } = null!;
}
