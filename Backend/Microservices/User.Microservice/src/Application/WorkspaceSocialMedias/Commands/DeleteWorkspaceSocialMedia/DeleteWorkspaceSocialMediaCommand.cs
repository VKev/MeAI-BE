using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.WorkspaceSocialMedias.Commands.DeleteWorkspaceSocialMedia;

public sealed record DeleteWorkspaceSocialMediaCommand(
    [Required] Guid WorkspaceId,
    [Required] Guid SocialMediaId,
    [Required] Guid UserId) : ICommand;
