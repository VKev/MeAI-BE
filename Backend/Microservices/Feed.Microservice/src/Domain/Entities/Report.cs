using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Report
{
    [Key]
    public Guid Id { get; set; }

    public Guid ReporterId { get; set; }

    public string TargetType { get; set; } = null!; // "Post" or "Comment"

    public Guid TargetId { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = "Pending"; // "Pending", "Reviewed", "Resolved"

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
