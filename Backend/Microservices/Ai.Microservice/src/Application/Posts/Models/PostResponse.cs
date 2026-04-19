using Domain.Entities;

namespace Application.Posts.Models;

public sealed record PostResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string? AvatarUrl,
    Guid? WorkspaceId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status,
    bool IsPublished,
    IReadOnlyList<PostMediaResponse> Media,
    IReadOnlyList<PostPublicationResponse> Publications,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record PostMediaResponse(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType);

public sealed record PostPublicationResponse(
    Guid Id,
    Guid SocialMediaId,
    string SocialMediaType,
    string DestinationOwnerId,
    string ExternalContentId,
    string ExternalContentIdType,
    string ContentType,
    string PublishStatus,
    DateTime? PublishedAt,
    DateTime CreatedAt);
