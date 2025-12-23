namespace Domain.Entities;

public sealed class Workspace
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string WorkspaceName { get; set; } = null!;

    public string? WorkspaceType { get; set; }

    public string? Description { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
