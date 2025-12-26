using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.SocialMedias.Commands.DeleteSocialMedia;

public sealed record DeleteSocialMediaCommand(
    [Required] Guid SocialMediaId,
    [Required] Guid UserId) : ICommand;
