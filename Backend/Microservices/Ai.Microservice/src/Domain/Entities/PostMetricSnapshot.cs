using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PostMetricSnapshot
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string Platform { get; set; } = string.Empty;

    public string PlatformPostId { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string? PostPayloadJson { get; set; }

    public long? ViewCount { get; set; }

    public long? LikeCount { get; set; }

    public long? CommentCount { get; set; }

    public long? ReplyCount { get; set; }

    public long? ShareCount { get; set; }

    public long? RepostCount { get; set; }

    [Column(TypeName = "jsonb")]
    public string? RawMetricsJson { get; set; }

    public long? QuoteCount { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime RetrievedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
