using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.SocialMedias.Queries.GetSocialMediaById;

public sealed record GetSocialMediaByIdQuery(
    [Required] Guid SocialMediaId,
    [Required] Guid UserId) : IQuery<SocialMediaResponse>;
