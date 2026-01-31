using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;

namespace Application.SocialMedias;

internal static class SocialMediaMapping
{
    internal static SocialMediaResponse ToResponse(SocialMedia socialMedia, SocialMediaUserProfile? profile = null) =>
        new(
            socialMedia.Id,
            socialMedia.Type,
            profile,
            socialMedia.CreatedAt,
            socialMedia.UpdatedAt,
            socialMedia.Metadata?.RootElement.GetRawText());
}
