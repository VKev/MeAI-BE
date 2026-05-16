using System.ComponentModel;
using Domain.Entities;

namespace Application.Posts.Models;

public sealed record PostResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string? AvatarUrl,
    Guid? WorkspaceId,
    Guid? PostBuilderId,
    Guid? ChatSessionId,
    Guid? SocialMediaId,
    [property: Description("Draft target platform for the post, for example facebook, instagram, tiktok, or threads.")]
    string? Platform,
    string? Title,
    PostContent? Content,
    string? Status,
    PostScheduleResponse? Schedule,
    bool IsPublished,
    IReadOnlyList<PostMediaResponse> Media,
    IReadOnlyList<PostPublicationResponse> Publications,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    bool IsAiRecommendedDraft = false,
    Guid? AiRecommendationCorrelationId = null,
    string? AiRecommendationStatus = null,
    bool IsAiRecommendationDone = false,
    DateTime? AiRecommendationCompletedAt = null,
    string? AiRecommendationErrorCode = null,
    string? AiRecommendationErrorMessage = null,
    Guid? AiImproveRecommendPostId = null,
    Guid? AiImproveCorrelationId = null,
    string? AiImproveStatus = null,
    bool IsAiImproving = false,
    bool IsAiImproveDone = false,
    DateTime? AiImproveCompletedAt = null,
    string? AiImproveErrorCode = null,
    string? AiImproveErrorMessage = null);

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
