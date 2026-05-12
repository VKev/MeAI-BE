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
    SocialPlatformPostStatsResponse? Stats,
    string? VideoDownloadUrl = null);

public sealed record SocialPlatformPostAnalyticsResponse(
    Guid SocialMediaId,
    string Platform,
    string PlatformPostId,
    SocialPlatformPostSummaryResponse Post,
    SocialPlatformPostStatsResponse Stats,
    SocialPlatformPostAnalysisResponse Analysis,
    DateTimeOffset RetrievedAt,
    SocialPlatformAccountInsightsResponse? AccountInsights = null,
    IReadOnlyList<SocialPlatformCommentResponse>? CommentSamples = null,
    IReadOnlyDictionary<string, long>? AdditionalMetrics = null);

public sealed record SocialPlatformDashboardSummaryResponse(
    Guid SocialMediaId,
    string Platform,
    int FetchedPostCount,
    bool HasMorePosts,
    string? NextCursor,
    string? LatestPublishedPostId,
    DateTimeOffset? LatestPublishedAt,
    SocialPlatformPostStatsResponse AggregatedStats,
    SocialPlatformPostAnalysisResponse? LatestAnalysis,
    SocialPlatformAccountInsightsResponse? AccountInsights,
    IReadOnlyList<SocialPlatformDashboardPostResponse> Posts);

public sealed record BatchDashboardSummaryRequest(
    List<Guid> SocialMediaIds,
    int? PostLimit = null);

public sealed record SocialPlatformDashboardPostResponse(
    SocialPlatformPostSummaryResponse Post,
    SocialPlatformPostAnalysisResponse? Analysis);

public sealed record SocialPlatformPostStatsResponse(
    long? Views,
    long? Reach = null,
    long? Impressions = null,
    long? Likes = null,
    long? Comments = null,
    long? Replies = null,
    long? Shares = null,
    long? Reposts = null,
    long? Quotes = null,
    long TotalInteractions = 0,
    long? Saves = null,
    IReadOnlyDictionary<string, long>? ReactionBreakdown = null,
    IReadOnlyDictionary<string, long>? MetricBreakdown = null);

public sealed record SocialPlatformPostAnalysisResponse(
    decimal? EngagementRateByViews,
    decimal? ConversationRateByViews,
    decimal? AmplificationRateByViews,
    decimal? ApprovalRateByViews,
    string PerformanceBand,
    IReadOnlyList<string> Highlights);

public sealed record SocialPlatformAccountInsightsResponse(
    string? AccountId,
    string? AccountName,
    string? Username,
    long? Followers,
    long? Following,
    long? MediaCount,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record SocialPlatformCommentResponse(
    string Id,
    string? Text,
    string? AuthorId,
    string? AuthorName,
    string? AuthorUsername,
    DateTimeOffset? CreatedAt,
    long? LikeCount,
    long? ReplyCount,
    string? Permalink);
