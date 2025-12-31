using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Domain.Entities;

public sealed class SocialMedia
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Type { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public JsonDocument? Metadata { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
