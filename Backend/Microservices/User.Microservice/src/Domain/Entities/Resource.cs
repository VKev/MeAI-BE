using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Resource
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public string Link { get; set; } = null!;

    public string? Status { get; set; }

    public string? ResourceType { get; set; }

    public string? ContentType { get; set; }

    public long? SizeBytes { get; set; }

    public string? StorageProvider { get; set; }

    public string? StorageBucket { get; set; }

    public string? StorageRegion { get; set; }

    public string? StorageNamespace { get; set; }

    public string? StorageKey { get; set; }

    public string? OriginalFileName { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastVerifiedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedFromStorageAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? ExpiresAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
