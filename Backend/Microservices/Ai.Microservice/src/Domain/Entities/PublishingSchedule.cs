using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PublishingSchedule
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public string? Name { get; set; }

    public string? Mode { get; set; }

    public string? Status { get; set; }

    public string? Timezone { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime ExecuteAtUtc { get; set; }

    public bool? IsPrivate { get; set; }

    public string? CreatedBy { get; set; }

    public string? PlatformPreference { get; set; }

    public string? AgentPrompt { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ExecutionContextJson { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastExecutionAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? NextRetryAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public ICollection<PublishingScheduleItem> Items { get; set; } = new List<PublishingScheduleItem>();

    public ICollection<PublishingScheduleTarget> Targets { get; set; } = new List<PublishingScheduleTarget>();
}
