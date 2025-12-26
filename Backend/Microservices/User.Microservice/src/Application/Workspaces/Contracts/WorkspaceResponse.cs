namespace Application.Workspaces.Contracts;

public sealed record WorkspaceResponse(
    Guid Id,
    string Name,
    string? Type,
    string? Description,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
