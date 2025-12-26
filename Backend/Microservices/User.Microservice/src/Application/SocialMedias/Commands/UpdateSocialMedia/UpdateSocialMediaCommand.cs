using System.Text.Json;
using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.SocialMedias.Commands.UpdateSocialMedia;

public sealed record UpdateSocialMediaCommand(
    [Required] Guid SocialMediaId,
    [Required] Guid UserId,
    [Required] string Type,
    JsonDocument? Metadata) : ICommand<SocialMediaResponse>;
