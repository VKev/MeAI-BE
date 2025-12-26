using Application.Workspaces.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.Workspaces.Commands.CreateWorkspace;

public sealed record CreateWorkspaceCommand(
    [Required] Guid UserId,
    [Required] string Name,
    string? Type,
    string? Description) : ICommand<WorkspaceResponse>;
