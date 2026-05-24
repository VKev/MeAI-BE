using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class GenerationSocialPreset
{
    [Key]
    public Guid Id { get; set; }

    public string Mode { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public string Label { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public string ContentLabel { get; set; } = null!;

    public string SupportedRatiosJson { get; set; } = "[]";

    public string DefaultRatio { get; set; } = null!;

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
