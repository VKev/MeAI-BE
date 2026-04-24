using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class ApiCredential
{
    [Key]
    public Guid Id { get; set; }

    public string ServiceName { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string KeyName { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string ValueEncrypted { get; set; } = null!;

    public string? ValueLast4 { get; set; }

    public bool IsActive { get; set; }

    public string Source { get; set; } = null!;

    public int Version { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastSyncedFromEnvAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastRotatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
