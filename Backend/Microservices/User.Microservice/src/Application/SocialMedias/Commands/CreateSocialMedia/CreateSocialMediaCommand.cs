using System.Text.Json;
using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.SocialMedias.Commands.CreateSocialMedia;

public sealed record CreateSocialMediaCommand(
    [Required] Guid UserId,
    [Required] string Type,
    JsonDocument? Metadata) : ICommand<SocialMediaResponse>;
