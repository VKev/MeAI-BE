using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(UserId), nameof(CreatedAt), nameof(Id))]
public sealed class Resource
{
    [Key] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [Required]
    public string Link { get; set; } = null!;

    public string? Status { get; set; }

    public string? ResourceType { get; set; }

    public string? ContentType { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamptz")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
