using Application.Workspaces.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Workspaces.Commands.UpdateWorkspace;

public sealed record UpdateWorkspaceCommand(
    [Required] Guid WorkspaceId,
    [Required] Guid UserId,
    [Required] string Name,
    string? Type,
    string? Description) : ICommand<WorkspaceResponse>;
