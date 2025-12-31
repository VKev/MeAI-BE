using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Workspace
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string WorkspaceName { get; set; } = null!;

    public string? WorkspaceType { get; set; }

    public string? Description { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
