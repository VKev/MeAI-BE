using Application.Workspaces.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Workspaces.Queries.GetWorkspaceById;

public sealed record GetWorkspaceByIdQuery(
    [Required] Guid WorkspaceId,
    [Required] Guid UserId) : IQuery<WorkspaceResponse>;
