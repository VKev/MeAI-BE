using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class EmailTemplate
{
    [Key]
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
