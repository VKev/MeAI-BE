using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(Key), IsUnique = true)]
public sealed class EmailTemplate
{
    [Key] public Guid Id { get; set; }

    [Required]
    public string Key { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "timestamp")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamp")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? DeletedAt { get; set; }
}
