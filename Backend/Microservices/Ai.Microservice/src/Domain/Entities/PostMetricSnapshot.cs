using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Domain.Entities;

public sealed class PostMetricSnapshot
{
    [Key]
    public Guid Id { get; set; }

    public Guid PostPublicationId { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CapturedAt { get; set; }

    public string MetricWindow { get; set; } = null!;

    public long? ViewCount { get; set; }

    public long? LikeCount { get; set; }

    public long? CommentCount { get; set; }

    public long? ShareCount { get; set; }

    public long? SaveCount { get; set; }

    public long? ImpressionCount { get; set; }

    public long? ReachCount { get; set; }

    public long? WatchTimeSeconds { get; set; }

    [Column(TypeName = "jsonb")]
    public JsonDocument? RawMetrics { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}
