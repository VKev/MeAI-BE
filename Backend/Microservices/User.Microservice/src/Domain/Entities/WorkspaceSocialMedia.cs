using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(UserId), nameof(WorkspaceId), nameof(CreatedAt), nameof(Id))]
public sealed class WorkspaceSocialMedia
{
    [Key] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid SocialMediaId { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamptz")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
