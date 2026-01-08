using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class VideoTask
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid CorrelationId { get; set; }

    public string? VeoTaskId { get; set; }

    public string Prompt { get; set; } = null!;

    public string Model { get; set; } = "veo3_fast";

    public string AspectRatio { get; set; } = "16:9";

    public string Status { get; set; } = "Submitted";

    [Column(TypeName = "jsonb")]
    public string? ResultUrls { get; set; }

    public string? Resolution { get; set; }

    public string? ErrorMessage { get; set; }

    public int? ErrorCode { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CompletedAt { get; set; }
}
