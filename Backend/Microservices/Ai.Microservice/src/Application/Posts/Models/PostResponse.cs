using Domain.Entities;

namespace Application.Posts.Models;

public sealed record PostResponse(
    Guid Id,
    Guid UserId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
