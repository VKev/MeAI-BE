namespace Domain.Entities;

public sealed class PostResource
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }

    public Guid ResourceId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Post Post { get; set; } = null!;
}
