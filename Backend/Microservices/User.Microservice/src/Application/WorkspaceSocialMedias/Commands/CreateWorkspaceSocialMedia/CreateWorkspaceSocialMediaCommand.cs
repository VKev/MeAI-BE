using System.Text.Json;
using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.WorkspaceSocialMedias.Commands.CreateWorkspaceSocialMedia;

public sealed record CreateWorkspaceSocialMediaCommand(
    [Required] Guid WorkspaceId,
    [Required] Guid UserId,
    [Required] string Type,
    JsonDocument? Metadata) : ICommand<SocialMediaResponse>;
