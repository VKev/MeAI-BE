using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PublishingScheduleTarget
{
    [Key]
    public Guid Id { get; set; }

    public Guid ScheduleId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string? Platform { get; set; }

    public string? TargetLabel { get; set; }

    public bool IsPrimary { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public PublishingSchedule? Schedule { get; set; }
}
