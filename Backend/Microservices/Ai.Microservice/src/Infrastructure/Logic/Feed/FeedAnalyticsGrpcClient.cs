using Application.Abstractions.Feed;
using Application.Posts;
using Application.Posts.Models;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.FeedAnalytics;
using SharedLibrary.Grpc.FeedPosts;

namespace Infrastructure.Logic.Feed;

public sealed class FeedAnalyticsGrpcClient : IFeedAnalyticsService
{
    private const string FeedPlatform = "feed";

    private readonly FeedAnalyticsService.FeedAnalyticsServiceClient _client;

    public FeedAnalyticsGrpcClient(FeedAnalyticsService.FeedAnalyticsServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<SocialPlatformDashboardSummaryResponse>> GetDashboardSummaryAsync(
        Guid requesterUserId,
        string username,
        int? postLimit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("FeedAnalytics.InvalidUsername", "Username is required."));
        }

        var request = new GetFeedDashboardSummaryRequest
        {
            RequesterUserId = requesterUserId.ToString(),
            Username = username.Trim(),
            PostLimit = postLimit ?? 0
        };

        try
        {
            var response = await _client.GetDashboardSummaryAsync(request, cancellationToken: cancellationToken);
            return Result.Success(MapDashboardSummary(requesterUserId, response));
        }
        catch (RpcException ex)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(MapGrpcError(ex, "FeedAnalytics.DashboardSummaryFailed"));
        }
    }

    public async Task<Result<SocialPlatformPostAnalyticsResponse>> GetPostAnalyticsAsync(
        Guid requesterUserId,
        Guid postId,
        int? commentSampleLimit,
        CancellationToken cancellationToken)
    {
        if (postId == Guid.Empty)
        {
            return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                new Error("FeedAnalytics.InvalidPostId", "Post id is required."));
        }

        var request = new GetFeedPostAnalyticsRequest
        {
            RequesterUserId = requesterUserId.ToString(),
            PostId = postId.ToString(),
            CommentSampleLimit = commentSampleLimit ?? 0
        };

        try
        {
            var response = await _client.GetPostAnalyticsAsync(request, cancellationToken: cancellationToken);
            return Result.Success(MapPostAnalytics(requesterUserId, response));
        }
        catch (RpcException ex)
        {
            return Result.Failure<SocialPlatformPostAnalyticsResponse>(MapGrpcError(ex, "FeedAnalytics.PostAnalyticsFailed"));
        }
    }

    private static SocialPlatformDashboardSummaryResponse MapDashboardSummary(
        Guid requesterUserId,
        GetFeedDashboardSummaryResponse response)
    {
        var posts = response.Posts.Select(MapDashboardPost).ToList();
        var latestPost = posts.FirstOrDefault();

        return new SocialPlatformDashboardSummaryResponse(
            SocialMediaId: requesterUserId,
            Platform: FeedPlatform,
            FetchedPostCount: response.FetchedPostCount,
            HasMorePosts: response.HasMorePosts,
            NextCursor: null,
            LatestPublishedPostId: string.IsNullOrWhiteSpace(response.LatestPublishedPostId) ? null : response.LatestPublishedPostId,
            LatestPublishedAt: ParseDateTimeOffset(response.LatestPublishedAt),
            AggregatedStats: MapStats(response.AggregatedStats),
            LatestAnalysis: latestPost?.Analysis,
            AccountInsights: MapAccountInsights(response.Profile),
            Posts: posts);
    }

    private static SocialPlatformPostAnalyticsResponse MapPostAnalytics(
        Guid requesterUserId,
        GetFeedPostAnalyticsResponse response)
    {
        var post = MapPostSummary(response.Post);
        var stats = MapStats(response.Post.Stats);

        return new SocialPlatformPostAnalyticsResponse(
            SocialMediaId: requesterUserId,
            Platform: FeedPlatform,
            PlatformPostId: response.Post.PostId,
            Post: post,
            Stats: stats,
            Analysis: SocialPlatformPostAnalysisFactory.Create(stats),
            RetrievedAt: DateTimeOffset.UtcNow,
            AccountInsights: MapAccountInsights(response.Profile),
            CommentSamples: response.CommentSamples.Select(MapComment).ToList(),
            AdditionalMetrics: CreateAdditionalMetrics(response.Post.Stats));
    }

    private static SocialPlatformDashboardPostResponse MapDashboardPost(FeedDashboardPost post)
    {
        var summary = MapPostSummary(post);
        var analysis = summary.Stats is null ? null : SocialPlatformPostAnalysisFactory.Create(summary.Stats);
        return new SocialPlatformDashboardPostResponse(summary, analysis);
    }

    private static SocialPlatformPostSummaryResponse MapPostSummary(FeedDashboardPost post)
    {
        return new SocialPlatformPostSummaryResponse(
            PlatformPostId: post.PostId,
            Title: null,
            Text: string.IsNullOrWhiteSpace(post.Content) ? null : post.Content,
            Description: null,
            MediaType: string.IsNullOrWhiteSpace(post.MediaType) ? null : post.MediaType,
            MediaUrl: string.IsNullOrWhiteSpace(post.MediaUrl) ? null : post.MediaUrl,
            ThumbnailUrl: string.IsNullOrWhiteSpace(post.MediaUrl) ? null : post.MediaUrl,
            Permalink: null,
            ShareUrl: null,
            EmbedUrl: null,
            DurationSeconds: null,
            PublishedAt: ParseDateTimeOffset(post.CreatedAt),
            Stats: MapStats(post.Stats));
    }

    private static SocialPlatformPostStatsResponse MapStats(FeedAnalyticsStats stats)
    {
        return new SocialPlatformPostStatsResponse(
            Views: null,
            Reach: null,
            Impressions: null,
            Likes: stats.Likes,
            Comments: stats.TopLevelComments,
            Replies: stats.Replies,
            Shares: null,
            Reposts: null,
            Quotes: null,
            TotalInteractions: stats.TotalInteractions,
            Saves: null,
            ReactionBreakdown: null,
            MetricBreakdown: CreateMetricBreakdown(stats));
    }

    private static SocialPlatformAccountInsightsResponse MapAccountInsights(FeedAnalyticsProfile profile)
    {
        return new SocialPlatformAccountInsightsResponse(
            AccountId: string.IsNullOrWhiteSpace(profile.UserId) ? null : profile.UserId,
            AccountName: string.IsNullOrWhiteSpace(profile.FullName) ? null : profile.FullName,
            Username: string.IsNullOrWhiteSpace(profile.Username) ? null : profile.Username,
            Followers: profile.FollowersCount,
            Following: profile.FollowingCount,
            MediaCount: profile.MediaCount,
            Metadata: CreateProfileMetadata(profile));
    }

    private static SocialPlatformCommentResponse MapComment(FeedCommentSample comment)
    {
        return new SocialPlatformCommentResponse(
            Id: comment.CommentId,
            Text: string.IsNullOrWhiteSpace(comment.Content) ? null : comment.Content,
            AuthorId: string.IsNullOrWhiteSpace(comment.UserId) ? null : comment.UserId,
            AuthorName: string.IsNullOrWhiteSpace(comment.Username) ? null : comment.Username,
            AuthorUsername: string.IsNullOrWhiteSpace(comment.Username) ? null : comment.Username,
            CreatedAt: ParseDateTimeOffset(comment.CreatedAt),
            LikeCount: comment.LikesCount,
            ReplyCount: comment.RepliesCount,
            Permalink: null);
    }

    private static IReadOnlyDictionary<string, long> CreateMetricBreakdown(FeedAnalyticsStats stats)
    {
        return new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["likes"] = stats.Likes,
            ["topLevelComments"] = stats.TopLevelComments,
            ["replies"] = stats.Replies,
            ["totalDiscussion"] = stats.TotalDiscussion,
            ["totalInteractions"] = stats.TotalInteractions,
            ["mediaCount"] = stats.MediaCount,
            ["hashtagCount"] = stats.HashtagCount
        };
    }

    private static IReadOnlyDictionary<string, long> CreateAdditionalMetrics(FeedAnalyticsStats stats)
    {
        return new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["topLevelComments"] = stats.TopLevelComments,
            ["replies"] = stats.Replies,
            ["totalDiscussion"] = stats.TotalDiscussion,
            ["mediaCount"] = stats.MediaCount,
            ["hashtagCount"] = stats.HashtagCount
        };
    }

    private static IReadOnlyDictionary<string, string>? CreateProfileMetadata(FeedAnalyticsProfile profile)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
        {
            metadata["avatarUrl"] = profile.AvatarUrl;
        }

        if (!string.IsNullOrWhiteSpace(profile.FullName))
        {
            metadata["fullName"] = profile.FullName;
        }

        return metadata.Count == 0 ? null : metadata;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Error MapGrpcError(RpcException exception, string fallbackCode)
    {
        var code = exception.StatusCode switch
        {
            StatusCode.InvalidArgument => "FeedAnalytics.InvalidArgument",
            StatusCode.NotFound => "FeedAnalytics.NotFound",
            _ => fallbackCode
        };

        var description = string.IsNullOrWhiteSpace(exception.Status.Detail)
            ? "Feed analytics request failed."
            : exception.Status.Detail;

        return new Error(code, description);
    }
}
