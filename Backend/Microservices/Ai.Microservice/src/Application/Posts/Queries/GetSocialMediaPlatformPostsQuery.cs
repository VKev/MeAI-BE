using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetSocialMediaPlatformPostsQuery(
    Guid UserId,
    Guid SocialMediaId,
    string? Cursor,
    int? Limit) : IRequest<Result<SocialPlatformPostsResponse>>;

public sealed class GetSocialMediaPlatformPostsQueryHandler
    : IRequestHandler<GetSocialMediaPlatformPostsQuery, Result<SocialPlatformPostsResponse>>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";

    private readonly IFacebookContentService _facebookContentService;
    private readonly IInstagramContentService _instagramContentService;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly ITikTokContentService _tikTokContentService;
    private readonly IThreadsContentService _threadsContentService;
    private readonly IPostMetricSnapshotRepository _postMetricSnapshotRepository;

    public GetSocialMediaPlatformPostsQueryHandler(
        IFacebookContentService facebookContentService,
        IInstagramContentService instagramContentService,
        IUserSocialMediaService userSocialMediaService,
        ITikTokContentService tikTokContentService,
        IThreadsContentService threadsContentService,
        IPostMetricSnapshotRepository postMetricSnapshotRepository)
    {
        _facebookContentService = facebookContentService;
        _instagramContentService = instagramContentService;
        _userSocialMediaService = userSocialMediaService;
        _tikTokContentService = tikTokContentService;
        _threadsContentService = threadsContentService;
        _postMetricSnapshotRepository = postMetricSnapshotRepository;
    }

    public async Task<Result<SocialPlatformPostsResponse>> Handle(
        GetSocialMediaPlatformPostsQuery request,
        CancellationToken cancellationToken)
    {
        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<SocialPlatformPostsResponse>(socialMediaResult.Error);
        }

        var socialMedia = socialMediaResult.Value.FirstOrDefault();
        if (socialMedia == null)
        {
            return Result.Failure<SocialPlatformPostsResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        using var metadata = SocialMediaMetadataHelper.Parse(socialMedia.MetadataJson);

        if (string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var userAccessToken = SocialMediaMetadataHelper.GetString(metadata, "user_access_token")
                                  ?? SocialMediaMetadataHelper.GetString(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(userAccessToken))
            {
                return Result.Failure<SocialPlatformPostsResponse>(
                    new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
            }

            var postsResult = await _facebookContentService.GetPostsAsync(
                new FacebookPostListRequest(
                    UserAccessToken: userAccessToken,
                    PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                    PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token"),
                    Limit: request.Limit,
                    Cursor: request.Cursor),
                cancellationToken);

            if (postsResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostsResponse>(postsResult.Error);
            }

            var hydratedPosts = await HydrateFacebookPostsAsync(
                userAccessToken,
                SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                SocialMediaMetadataHelper.GetString(metadata, "page_access_token"),
                postsResult.Value.Posts,
                cancellationToken);

            var items = hydratedPosts
                .Select(post => new SocialPlatformPostSummaryResponse(
                    PlatformPostId: post.Id,
                    Title: post.AttachmentTitle,
                    Text: post.Message ?? post.Story,
                    Description: post.AttachmentDescription,
                    MediaType: post.MediaType,
                    MediaUrl: post.MediaUrl ?? post.FullPictureUrl,
                    ThumbnailUrl: post.ThumbnailUrl ?? post.FullPictureUrl,
                    Permalink: post.PermalinkUrl,
                    ShareUrl: post.PermalinkUrl,
                    EmbedUrl: null,
                    DurationSeconds: null,
                    PublishedAt: ToDateTimeOffset(post.CreatedTime),
                    Stats: new SocialPlatformPostStatsResponse(
                        Views: post.ViewCount,
                        Reach: post.ReachCount,
                        Impressions: post.ImpressionCount,
                        Likes: post.ReactionCount,
                        Comments: post.CommentCount,
                        Replies: null,
                        Shares: post.ShareCount,
                        Reposts: null,
                        Quotes: null,
                        TotalInteractions: (post.ReactionCount ?? 0) + (post.CommentCount ?? 0) + (post.ShareCount ?? 0),
                        ReactionBreakdown: post.ReactionBreakdown,
                        MetricBreakdown: new Dictionary<string, long>
                        {
                            ["views"] = post.ViewCount ?? 0,
                            ["reach"] = post.ReachCount ?? 0,
                            ["impressions"] = post.ImpressionCount ?? 0,
                            ["likes"] = post.ReactionCount ?? 0,
                            ["comments"] = post.CommentCount ?? 0,
                            ["shares"] = post.ShareCount ?? 0
                        }),
                    VideoDownloadUrl: post.VideoSourceUrl))
                .ToList();

            return Result.Success(new SocialPlatformPostsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: FacebookType,
                NextCursor: postsResult.Value.NextCursor,
                HasMore: postsResult.Value.HasMore,
                Items: items));
        }

        if (string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<SocialPlatformPostsResponse>(
                    new Error("TikTok.InvalidToken", "Access token not found in social media metadata."));
            }

            if (!SocialMediaMetadataHelper.HasScope(metadata, "video.list"))
            {
                return Result.Failure<SocialPlatformPostsResponse>(
                    new Error("TikTok.InsufficientScope", "TikTok account must be reconnected with the video.list scope."));
            }

            long? cursor = null;
            if (!string.IsNullOrWhiteSpace(request.Cursor))
            {
                if (!long.TryParse(request.Cursor, out var parsedCursor))
                {
                    return Result.Failure<SocialPlatformPostsResponse>(
                        new Error("TikTok.InvalidCursor", "TikTok cursor must be a numeric value."));
                }

                cursor = parsedCursor;
            }

            var videosResult = await _tikTokContentService.GetVideosAsync(
                new TikTokVideoListRequest(
                    AccessToken: accessToken,
                    Cursor: cursor,
                    MaxCount: request.Limit),
                cancellationToken);

            if (videosResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostsResponse>(videosResult.Error);
            }

            var items = videosResult.Value.Videos
                .Select(video =>
                    new SocialPlatformPostSummaryResponse(
                        PlatformPostId: video.Id,
                        Title: video.Title,
                        Text: null,
                        Description: video.VideoDescription,
                        MediaType: "video",
                        MediaUrl: null,
                        ThumbnailUrl: video.CoverImageUrl,
                        Permalink: video.ShareUrl,
                        ShareUrl: video.ShareUrl,
                        EmbedUrl: video.EmbedLink,
                        DurationSeconds: video.Duration,
                        PublishedAt: ToUnixTime(video.CreateTime),
                        Stats: new SocialPlatformPostStatsResponse(
                            Views: video.ViewCount,
                            Reach: video.ViewCount,
                            Impressions: video.ViewCount,
                            Likes: video.LikeCount,
                            Comments: video.CommentCount,
                            Replies: null,
                            Shares: video.ShareCount,
                            Reposts: null,
                            Quotes: null,
                            TotalInteractions: (video.LikeCount ?? 0) + (video.CommentCount ?? 0) + (video.ShareCount ?? 0),
                            MetricBreakdown: new Dictionary<string, long>
                            {
                                ["views"] = video.ViewCount ?? 0,
                                ["likes"] = video.LikeCount ?? 0,
                                ["comments"] = video.CommentCount ?? 0,
                                ["shares"] = video.ShareCount ?? 0
                            })))
                .ToList();

            var metrics = await _postMetricSnapshotRepository.GetLatestByPlatformPostIdsAsync(
                request.UserId,
                request.SocialMediaId,
                items.Select(item => item.PlatformPostId).ToArray(),
                cancellationToken);

            var statsLookup = SocialPlatformPostMetricSnapshotMapper.ToStatsLookup(metrics);
            var enrichedItems = items
                .Select(item => item with
                {
                    Stats = MergeStats(
                        item.Stats,
                        statsLookup.TryGetValue(item.PlatformPostId, out var cachedStats)
                            ? cachedStats
                            : null)
                })
                .ToList();

            return Result.Success(new SocialPlatformPostsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: TikTokType,
                NextCursor: videosResult.Value.HasMore ? videosResult.Value.Cursor?.ToString() : null,
                HasMore: videosResult.Value.HasMore,
                Items: enrichedItems));
        }

        if (string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token")
                              ?? SocialMediaMetadataHelper.GetString(metadata, "user_access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<SocialPlatformPostsResponse>(
                    new Error("Instagram.InvalidToken", "Access token not found in social media metadata."));
            }

            var instagramUserId = SocialMediaMetadataHelper.GetString(metadata, "instagram_business_account_id")
                                  ?? SocialMediaMetadataHelper.GetString(metadata, "user_id")
                                  ?? SocialMediaMetadataHelper.GetString(metadata, "id");
            if (string.IsNullOrWhiteSpace(instagramUserId))
            {
                return Result.Failure<SocialPlatformPostsResponse>(
                    new Error("Instagram.InvalidAccount", "Instagram business account id is missing in social media metadata."));
            }

            var postsResult = await _instagramContentService.GetPostsAsync(
                new InstagramPostListRequest(
                    AccessToken: accessToken,
                    InstagramUserId: instagramUserId,
                    Limit: request.Limit,
                    Cursor: request.Cursor),
                cancellationToken);

            if (postsResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostsResponse>(postsResult.Error);
            }

            var items = postsResult.Value.Posts
                .Select(post => new SocialPlatformPostSummaryResponse(
                    PlatformPostId: post.Id,
                    Title: null,
                    Text: post.Caption,
                    Description: null,
                    MediaType: post.MediaType,
                    MediaUrl: post.MediaUrl,
                    ThumbnailUrl: post.ThumbnailUrl,
                    Permalink: post.Permalink,
                    ShareUrl: post.Permalink,
                    EmbedUrl: null,
                    DurationSeconds: null,
                    PublishedAt: ToDateTimeOffset(post.Timestamp),
                    Stats: new SocialPlatformPostStatsResponse(
                        Views: null,
                        Likes: post.LikeCount,
                        Comments: post.CommentCount,
                        Replies: null,
                        Shares: null,
                        Reposts: null,
                        Quotes: null,
                        TotalInteractions: (post.LikeCount ?? 0) + (post.CommentCount ?? 0))))
                .ToList();

            var metrics = await _postMetricSnapshotRepository.GetLatestByPlatformPostIdsAsync(
                request.UserId,
                request.SocialMediaId,
                items.Select(item => item.PlatformPostId).ToArray(),
                cancellationToken);

            var statsLookup = SocialPlatformPostMetricSnapshotMapper.ToStatsLookup(metrics);
            var enrichedItems = items
                .Select(item => item with
                {
                    Stats = MergeStats(
                        item.Stats,
                        statsLookup.TryGetValue(item.PlatformPostId, out var cachedStats)
                            ? cachedStats
                            : null)
                })
                .ToList();

            return Result.Success(new SocialPlatformPostsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: InstagramType,
                NextCursor: postsResult.Value.NextCursor,
                HasMore: postsResult.Value.HasMore,
                Items: enrichedItems));
        }

        if (string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<SocialPlatformPostsResponse>(
                    new Error("Threads.InvalidToken", "Access token not found in social media metadata."));
            }

            var postsResult = await _threadsContentService.GetPostsAsync(
                new ThreadsPostListRequest(
                    AccessToken: accessToken,
                    Limit: request.Limit,
                    Cursor: request.Cursor),
                cancellationToken);

            if (postsResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostsResponse>(postsResult.Error);
            }

            var insightsByPostId = new Dictionary<string, ThreadsPostInsights?>(StringComparer.Ordinal);
            foreach (var post in postsResult.Value.Posts)
            {
                if (string.IsNullOrWhiteSpace(post.Id))
                {
                    continue;
                }

                var insightsResult = await _threadsContentService.GetPostInsightsAsync(
                    new ThreadsPostInsightsRequest(accessToken, post.Id),
                    cancellationToken);

                insightsByPostId[post.Id] = insightsResult.IsSuccess ? insightsResult.Value : null;
            }

            var items = postsResult.Value.Posts
                .Select(post =>
                {
                    insightsByPostId.TryGetValue(post.Id, out var insights);

                    var comments = insights?.Replies;
                    var stats = insights == null
                        ? null
                        : new SocialPlatformPostStatsResponse(
                            Views: insights.Views,
                            Likes: insights.Likes,
                            Comments: comments,
                            Replies: null,
                            Shares: insights.Shares,
                            Reposts: insights.Reposts,
                            Quotes: insights.Quotes,
                            TotalInteractions: (insights.Likes ?? 0) +
                                               (comments ?? 0) +
                                               (insights.Shares ?? 0) +
                                               (insights.Reposts ?? 0) +
                                               (insights.Quotes ?? 0));

                    return new SocialPlatformPostSummaryResponse(
                        PlatformPostId: post.Id,
                        Title: null,
                        Text: post.Text,
                        Description: null,
                        MediaType: post.MediaType,
                        MediaUrl: post.MediaUrl ?? post.GifUrl,
                        ThumbnailUrl: post.ThumbnailUrl ?? post.ProfilePictureUrl,
                        Permalink: post.Permalink,
                        ShareUrl: post.Permalink,
                        EmbedUrl: null,
                        DurationSeconds: null,
                        PublishedAt: ToDateTimeOffset(post.Timestamp),
                        Stats: stats);
                })
                .ToList();

            var metrics = await _postMetricSnapshotRepository.GetLatestByPlatformPostIdsAsync(
                request.UserId,
                request.SocialMediaId,
                items.Select(item => item.PlatformPostId).ToArray(),
                cancellationToken);

            var statsLookup = SocialPlatformPostMetricSnapshotMapper.ToStatsLookup(metrics);
            var enrichedItems = items
                .Select(item => item with
                {
                    Stats = MergeStats(
                        item.Stats,
                        statsLookup.TryGetValue(item.PlatformPostId, out var cachedStats)
                            ? cachedStats
                            : null)
                })
                .ToList();

            return Result.Success(new SocialPlatformPostsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: ThreadsType,
                NextCursor: postsResult.Value.NextCursor,
                HasMore: postsResult.Value.HasMore,
                Items: enrichedItems));
        }

        return Result.Failure<SocialPlatformPostsResponse>(
            new Error("SocialMedia.UnsupportedPlatform", "Only Facebook, Instagram, TikTok, and Threads social media accounts are supported."));
    }

    private static DateTimeOffset? ToUnixTime(long? unixSeconds)
    {
        if (unixSeconds is null or <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value);
    }

    private static DateTimeOffset? ToDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private async Task<IReadOnlyList<FacebookPostDetails>> HydrateFacebookPostsAsync(
        string userAccessToken,
        string? preferredPageId,
        string? preferredPageAccessToken,
        IReadOnlyList<FacebookPostDetails> posts,
        CancellationToken cancellationToken)
    {
        var tasks = posts.Select(post => HydrateFacebookPostAsync(
                userAccessToken,
                preferredPageId,
                preferredPageAccessToken,
                post,
                cancellationToken))
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<FacebookPostDetails> HydrateFacebookPostAsync(
        string userAccessToken,
        string? preferredPageId,
        string? preferredPageAccessToken,
        FacebookPostDetails post,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(post.Id))
        {
            return post;
        }

        var postResult = await _facebookContentService.GetPostAsync(
            new FacebookPostDetailsRequest(
                UserAccessToken: userAccessToken,
                PostId: post.Id,
                PreferredPageId: preferredPageId,
                PreferredPageAccessToken: preferredPageAccessToken),
            cancellationToken);

        return postResult.IsSuccess ? postResult.Value : post;
    }

    private static SocialPlatformPostStatsResponse? MergeStats(
        SocialPlatformPostStatsResponse? primary,
        SocialPlatformPostStatsResponse? fallback)
    {
        if (primary == null)
        {
            return fallback;
        }

        if (fallback == null)
        {
            return primary;
        }

        var likes = primary.Likes ?? fallback.Likes;
        var comments = primary.Comments ?? fallback.Comments;
        var replies = primary.Replies ?? fallback.Replies;
        var shares = primary.Shares ?? fallback.Shares;
        var reposts = primary.Reposts ?? fallback.Reposts;
        var quotes = primary.Quotes ?? fallback.Quotes;
        var reach = primary.Reach ?? fallback.Reach;
        var impressions = primary.Impressions ?? fallback.Impressions;
        var saves = primary.Saves ?? fallback.Saves;
        var reactionBreakdown = primary.ReactionBreakdown ?? fallback.ReactionBreakdown;
        var metricBreakdown = primary.MetricBreakdown ?? fallback.MetricBreakdown;

        return primary with
        {
            Views = primary.Views ?? fallback.Views,
            Reach = reach,
            Impressions = impressions,
            Likes = likes,
            Comments = comments,
            Replies = replies,
            Shares = shares,
            Reposts = reposts,
            Quotes = quotes,
            Saves = saves,
            ReactionBreakdown = reactionBreakdown,
            MetricBreakdown = metricBreakdown,
            TotalInteractions =
                (likes ?? 0) +
                (comments ?? 0) +
                (replies ?? 0) +
                (shares ?? 0) +
                (reposts ?? 0) +
                (quotes ?? 0)
        };
    }
}
