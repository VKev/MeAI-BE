using Application.Workspaces.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Workspaces.Queries.GetWorkspaces;

public sealed record GetWorkspacesQuery(
    [Required] Guid UserId) : IQuery<IReadOnlyList<WorkspaceResponse>>;
