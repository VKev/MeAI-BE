using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Workspaces.Commands.DeleteWorkspace;

public sealed record DeleteWorkspaceCommand(
    [Required] Guid WorkspaceId,
    [Required] Guid UserId) : ICommand;
