namespace Application.Analytics.Models;

public sealed record FeedAnalyticsProfileResponse(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl,
    long FollowersCount,
    long FollowingCount,
    long MediaCount);

public sealed record FeedPostStatsResponse(
    long Likes,
    long TopLevelComments,
    long Replies,
    long TotalDiscussion,
    long TotalInteractions,
    long MediaCount,
    long HashtagCount);

public sealed record FeedDashboardPostResponse(
    Guid PostId,
    Guid UserId,
    string? Content,
    string? MediaUrl,
    string? MediaType,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    FeedPostStatsResponse Stats);

public sealed record FeedCommentSampleResponse(
    Guid CommentId,
    Guid PostId,
    Guid UserId,
    string Username,
    string? AvatarUrl,
    string Content,
    long LikesCount,
    long RepliesCount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record FeedDashboardSummaryResponse(
    FeedAnalyticsProfileResponse Profile,
    int FetchedPostCount,
    bool HasMorePosts,
    Guid? LatestPublishedPostId,
    DateTimeOffset? LatestPublishedAt,
    FeedPostStatsResponse AggregatedStats,
    IReadOnlyList<FeedDashboardPostResponse> Posts);

public sealed record FeedPostAnalyticsResponse(
    FeedAnalyticsProfileResponse Profile,
    FeedDashboardPostResponse Post,
    IReadOnlyList<FeedCommentSampleResponse> CommentSamples);
