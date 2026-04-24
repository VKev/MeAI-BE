using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PublishingScheduleItem
{
    [Key]
    public Guid Id { get; set; }

    public Guid ScheduleId { get; set; }

    public string? ItemType { get; set; }

    public Guid ItemId { get; set; }

    public int SortOrder { get; set; }

    public string? ExecutionBehavior { get; set; }

    public string? Status { get; set; }

    public string? ErrorMessage { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastExecutionAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public PublishingSchedule? Schedule { get; set; }
}
