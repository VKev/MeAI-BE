namespace Domain.Entities;

public sealed class Post
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? SocialMediaId { get; set; }

    public string? Title { get; set; }

    public PostContent? Content { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public ICollection<PostResource> PostResources { get; set; } = new List<PostResource>();
}
