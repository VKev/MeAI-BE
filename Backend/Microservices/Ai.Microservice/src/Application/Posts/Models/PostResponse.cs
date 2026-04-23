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
    PostScheduleResponse? Schedule,
    bool IsPublished,
    IReadOnlyList<PostMediaResponse> Media,
    IReadOnlyList<PostPublicationResponse> Publications,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record PostScheduleInput(
    Guid? ScheduleGroupId,
    DateTime ScheduledAtUtc,
    string? Timezone,
    IReadOnlyList<Guid>? SocialMediaIds,
    bool? IsPrivate);

public sealed record PostScheduleResponse(
    Guid ScheduleGroupId,
    DateTime ScheduledAtUtc,
    string? Timezone,
    IReadOnlyList<Guid> SocialMediaIds,
    bool? IsPrivate);

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
