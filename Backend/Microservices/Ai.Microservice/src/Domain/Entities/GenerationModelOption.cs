using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class GenerationModelOption
{
    [Key]
    public Guid Id { get; set; }

    public string Mode { get; set; } = null!;

    public string ModelId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string SupportedRatiosJson { get; set; } = "[]";

    public string SupportedQualitiesJson { get; set; } = "[]";

    public bool SupportsResolution { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
