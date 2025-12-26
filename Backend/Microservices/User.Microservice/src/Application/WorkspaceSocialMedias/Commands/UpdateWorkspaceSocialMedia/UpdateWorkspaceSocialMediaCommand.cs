using System.Text.Json;
using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.WorkspaceSocialMedias.Commands.UpdateWorkspaceSocialMedia;

public sealed record UpdateWorkspaceSocialMediaCommand(
    [Required] Guid WorkspaceId,
    [Required] Guid SocialMediaId,
    [Required] Guid UserId,
    [Required] string Type,
    JsonDocument? Metadata) : ICommand<SocialMediaResponse>;
