using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(UserId), nameof(CreatedAt), nameof(Id))]
public sealed class SocialMedia
{
    [Key] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [Required]
    public string Type { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public JsonDocument? Metadata { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamptz")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
