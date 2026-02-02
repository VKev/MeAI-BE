using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class ImageTask
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid CorrelationId { get; set; }

    public string? KieTaskId { get; set; }

    public string Prompt { get; set; } = null!;

    public string AspectRatio { get; set; } = "1:1";

    public string Resolution { get; set; } = "1K";

    public string OutputFormat { get; set; } = "png";

    public string Status { get; set; } = "Submitted";

    [Column(TypeName = "jsonb")]
    public string? ResultUrls { get; set; }

    public string? ErrorMessage { get; set; }

    public int? ErrorCode { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CompletedAt { get; set; }
}
