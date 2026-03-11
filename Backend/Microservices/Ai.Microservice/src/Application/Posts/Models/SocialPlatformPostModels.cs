namespace Application.Posts.Models;

public sealed record SocialPlatformPostsResponse(
    Guid SocialMediaId,
    string Platform,
    string? NextCursor,
    bool HasMore,
    IReadOnlyList<SocialPlatformPostSummaryResponse> Items);

public sealed record SocialPlatformPostSummaryResponse(
    string PlatformPostId,
    string? Title,
    string? Text,
    string? Description,
    string? MediaType,
    string? MediaUrl,
    string? ThumbnailUrl,
    string? Permalink,
    string? ShareUrl,
    string? EmbedUrl,
    int? DurationSeconds,
    DateTimeOffset? PublishedAt,
    SocialPlatformPostStatsResponse? Stats);

public sealed record SocialPlatformPostAnalyticsResponse(
    Guid SocialMediaId,
    string Platform,
    string PlatformPostId,
    SocialPlatformPostSummaryResponse Post,
    SocialPlatformPostStatsResponse Stats,
    SocialPlatformPostAnalysisResponse Analysis,
    DateTimeOffset RetrievedAt);

public sealed record SocialPlatformPostStatsResponse(
    long? Views,
    long? Likes,
    long? Comments,
    long? Replies,
    long? Shares,
    long? Reposts,
    long? Quotes,
    long TotalInteractions);

public sealed record SocialPlatformPostAnalysisResponse(
    decimal? EngagementRateByViews,
    decimal? ConversationRateByViews,
    decimal? AmplificationRateByViews,
    decimal? ApprovalRateByViews,
    string PerformanceBand,
    IReadOnlyList<string> Highlights);
