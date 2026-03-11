using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PostAnalyticsSnapshot
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? PostId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string Platform { get; set; } = string.Empty;

    public string PlatformPostId { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string? PostPayloadJson { get; set; }

    [Column(TypeName = "jsonb")]
    public string? StatsPayloadJson { get; set; }

    [Column(TypeName = "jsonb")]
    public string? AnalysisPayloadJson { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime RetrievedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
