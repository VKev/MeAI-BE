using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class PostPublication
{
    [Key]
    public Guid Id { get; set; }

    public Guid PostId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string SocialMediaType { get; set; } = null!;

    public string DestinationOwnerId { get; set; } = null!;

    public string ExternalContentId { get; set; } = null!;

    public string ExternalContentIdType { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public string PublishStatus { get; set; } = null!;

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? PublishedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastMetricsSyncAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
