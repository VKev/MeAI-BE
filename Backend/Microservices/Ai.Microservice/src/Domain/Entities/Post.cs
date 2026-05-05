using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Post
{
    [Key]
    public Guid Id { get; set; }

    public Guid? PostBuilderId { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public Guid? ChatSessionId { get; set; }

    public Guid? SocialMediaId { get; set; }

    public string? Platform { get; set; }

    public string? Title { get; set; }

    [Column(TypeName = "jsonb")]
    public PostContent? Content { get; set; }

    public string? Status { get; set; }

    public Guid? ScheduleGroupId { get; set; }

    /// <summary>
    /// Foreign key to the most recent <see cref="RecommendPost"/> generated for this
    /// post. Nullable + unique — at most one active RecommendPost per Post. On
    /// re-improve the start command hard-deletes the existing RecommendPost, inserts
    /// a new one, and updates this field; there is no history of past suggestions.
    /// </summary>
    public Guid? RecommendPostId { get; set; }

    [Column(TypeName = "uuid[]")]
    public Guid[] ScheduledSocialMediaIds { get; set; } = Array.Empty<Guid>();

    public bool? ScheduledIsPrivate { get; set; }

    public string? ScheduleTimezone { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? ScheduledAtUtc { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public PostBuilder? PostBuilder { get; set; }

    public ChatSession? ChatSession { get; set; }
}
