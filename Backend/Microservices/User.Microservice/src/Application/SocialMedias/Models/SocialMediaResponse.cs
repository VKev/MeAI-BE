using Application.Abstractions.SocialMedia;

namespace Application.SocialMedias.Models;

public sealed record SocialMediaResponse(
    Guid Id,
    string Type,
    SocialMediaUserProfile? Profile,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
