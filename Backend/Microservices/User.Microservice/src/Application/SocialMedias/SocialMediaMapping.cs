using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;

namespace Application.SocialMedias;

internal static class SocialMediaMapping
{
    internal static SocialMediaResponse ToResponse(SocialMedia socialMedia, SocialMediaUserProfile? profile = null, bool includeMetadata = false) =>
        new(
            socialMedia.Id,
            socialMedia.Type,
            profile,
            socialMedia.CreatedAt,
            socialMedia.UpdatedAt,
            includeMetadata ? socialMedia.Metadata?.RootElement.GetRawText() : null);
}
