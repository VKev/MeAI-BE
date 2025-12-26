using Application.Workspaces.Contracts;
using Domain.Entities;

namespace Application.Workspaces;

internal static class WorkspaceMapping
{
    internal static WorkspaceResponse ToResponse(Workspace workspace) =>
        new(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            workspace.Description,
            workspace.CreatedAt,
            workspace.UpdatedAt);
}
