using Application.SocialMedias.Contracts;
using Domain.Entities;

namespace Application.SocialMedias;

internal static class SocialMediaMapping
{
    internal static SocialMediaResponse ToResponse(SocialMedia socialMedia) =>
        new(
            socialMedia.Id,
            socialMedia.Type,
            socialMedia.Metadata,
            socialMedia.CreatedAt,
            socialMedia.UpdatedAt);
}
