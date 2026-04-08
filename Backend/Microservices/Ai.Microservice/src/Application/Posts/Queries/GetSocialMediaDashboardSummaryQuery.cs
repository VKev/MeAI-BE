using System.Text.Json;
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

public sealed record GetSocialMediaDashboardSummaryQuery(
    Guid UserId,
    Guid SocialMediaId,
    int? PostLimit) : IRequest<Result<SocialPlatformDashboardSummaryResponse>>;

public sealed class GetSocialMediaDashboardSummaryQueryHandler
    : IRequestHandler<GetSocialMediaDashboardSummaryQuery, Result<SocialPlatformDashboardSummaryResponse>>
{
    private const int DefaultPostLimit = 5;
    private const int MaxPostLimit = 8;
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

    public GetSocialMediaDashboardSummaryQueryHandler(
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

    public async Task<Result<SocialPlatformDashboardSummaryResponse>> Handle(
        GetSocialMediaDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(socialMediaResult.Error);
        }

        var socialMedia = socialMediaResult.Value.FirstOrDefault();
        if (socialMedia == null)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        var postLimit = Math.Clamp(request.PostLimit ?? DefaultPostLimit, 1, MaxPostLimit);

        using var metadata = SocialMediaMetadataHelper.Parse(socialMedia.MetadataJson);
        if (metadata == null)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("SocialMedia.InvalidMetadata", "Social media metadata is missing or invalid."));
        }

        if (string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildFacebookSummaryAsync(request, metadata, postLimit, cancellationToken);
        }

        if (string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildTikTokSummaryAsync(request, metadata, postLimit, cancellationToken);
        }

        if (string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildInstagramSummaryAsync(request, metadata, postLimit, cancellationToken);
        }

        if (string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildThreadsSummaryAsync(request, metadata, postLimit, cancellationToken);
        }

        return Result.Failure<SocialPlatformDashboardSummaryResponse>(
            new Error("SocialMedia.UnsupportedPlatform", "Only Facebook, Instagram, TikTok, and Threads social media accounts are supported."));
    }

    private async Task<Result<SocialPlatformDashboardSummaryResponse>> BuildFacebookSummaryAsync(
        GetSocialMediaDashboardSummaryQuery request,
        JsonDocument metadata,
        int postLimit,
        CancellationToken cancellationToken)
    {
        var userAccessToken = SocialMediaMetadataHelper.GetString(metadata, "user_access_token")
                              ?? SocialMediaMetadataHelper.GetString(metadata, "access_token");
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
        }

        var postsTask = _facebookContentService.GetPostsAsync(
            new FacebookPostListRequest(
                UserAccessToken: userAccessToken,
                PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token"),
                Limit: postLimit,
                Cursor: null),
            cancellationToken);

        var accountInsightsTask = _facebookContentService.GetPageInsightsAsync(
            new FacebookPageInsightsRequest(
                UserAccessToken: userAccessToken,
                PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token")),
            cancellationToken);

        await postsTask;

        if (postsTask.Result.IsFailure)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(postsTask.Result.Error);
        }

        var hydratedPosts = await HydrateFacebookPostsAsync(
            userAccessToken,
            SocialMediaMetadataHelper.GetString(metadata, "page_id"),
            SocialMediaMetadataHelper.GetString(metadata, "page_access_token"),
            postsTask.Result.Value.Posts,
            cancellationToken);

        var posts = hydratedPosts
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
                    })))
            .ToList();

        var dashboardPosts = CreateDashboardPosts(posts);
        var accountInsightsResult = await accountInsightsTask;
        var accountInsights = accountInsightsResult.IsSuccess
            ? new SocialPlatformAccountInsightsResponse(
                AccountId: accountInsightsResult.Value.PageId,
                AccountName: accountInsightsResult.Value.Name,
                Username: null,
                Followers: accountInsightsResult.Value.Followers ?? accountInsightsResult.Value.Fans,
                Following: null,
                MediaCount: null)
            : null;

        return Result.Success(CreateSummaryResponse(
            request.SocialMediaId,
            FacebookType,
            postsTask.Result.Value.NextCursor,
            postsTask.Result.Value.HasMore,
            accountInsights,
            dashboardPosts));
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

    private async Task<Result<SocialPlatformDashboardSummaryResponse>> BuildTikTokSummaryAsync(
        GetSocialMediaDashboardSummaryQuery request,
        JsonDocument metadata,
        int postLimit,
        CancellationToken cancellationToken)
    {
        var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("TikTok.InvalidToken", "Access token not found in social media metadata."));
        }

        if (!SocialMediaMetadataHelper.HasScope(metadata, "video.list"))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("TikTok.InsufficientScope", "TikTok account must be reconnected with the video.list scope."));
        }

        var videosTask = _tikTokContentService.GetVideosAsync(
            new TikTokVideoListRequest(
                AccessToken: accessToken,
                Cursor: null,
                MaxCount: postLimit),
            cancellationToken);

        var accountInsightsTask = _tikTokContentService.GetAccountInsightsAsync(
            new TikTokAccountInsightsRequest(accessToken),
            cancellationToken);

        await videosTask;

        if (videosTask.Result.IsFailure)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(videosTask.Result.Error);
        }

        var posts = videosTask.Result.Value.Videos
            .Select(video => new SocialPlatformPostSummaryResponse(
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

        var enrichedPosts = await EnrichPostsWithCachedStatsAsync(
            request.UserId,
            request.SocialMediaId,
            posts,
            cancellationToken);

        var dashboardPosts = CreateDashboardPosts(enrichedPosts);
        var accountInsightsResult = await accountInsightsTask;
        var accountInsights = accountInsightsResult.IsSuccess
            ? new SocialPlatformAccountInsightsResponse(
                AccountId: accountInsightsResult.Value.OpenId,
                AccountName: accountInsightsResult.Value.DisplayName,
                Username: null,
                Followers: accountInsightsResult.Value.FollowerCount,
                Following: accountInsightsResult.Value.FollowingCount,
                MediaCount: accountInsightsResult.Value.VideoCount,
                Metadata: new Dictionary<string, string>
                {
                    ["likesCount"] = (accountInsightsResult.Value.LikesCount ?? 0).ToString(),
                    ["avatarUrl"] = accountInsightsResult.Value.AvatarUrl ?? string.Empty,
                    ["bio"] = accountInsightsResult.Value.BioDescription ?? string.Empty
                })
            : null;

        return Result.Success(CreateSummaryResponse(
            request.SocialMediaId,
            TikTokType,
            videosTask.Result.Value.HasMore ? videosTask.Result.Value.Cursor?.ToString() : null,
            videosTask.Result.Value.HasMore,
            accountInsights,
            dashboardPosts));
    }

    private async Task<Result<SocialPlatformDashboardSummaryResponse>> BuildInstagramSummaryAsync(
        GetSocialMediaDashboardSummaryQuery request,
        JsonDocument metadata,
        int postLimit,
        CancellationToken cancellationToken)
    {
        var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token")
                          ?? SocialMediaMetadataHelper.GetString(metadata, "user_access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("Instagram.InvalidToken", "Access token not found in social media metadata."));
        }

        var instagramUserId = SocialMediaMetadataHelper.GetString(metadata, "instagram_business_account_id")
                              ?? SocialMediaMetadataHelper.GetString(metadata, "user_id")
                              ?? SocialMediaMetadataHelper.GetString(metadata, "id");

        if (string.IsNullOrWhiteSpace(instagramUserId))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("Instagram.InvalidAccount", "Instagram business account id is missing in social media metadata."));
        }

        var postsTask = _instagramContentService.GetPostsAsync(
            new InstagramPostListRequest(
                AccessToken: accessToken,
                InstagramUserId: instagramUserId,
                Limit: postLimit,
                Cursor: null),
            cancellationToken);

        Task<Result<InstagramAccountInsights>>? accountInsightsTask = null;
        if (!string.IsNullOrWhiteSpace(instagramUserId))
        {
            accountInsightsTask = _instagramContentService.GetAccountInsightsAsync(
                new InstagramAccountInsightsRequest(accessToken, instagramUserId),
                cancellationToken);
        }

        await postsTask;

        if (postsTask.Result.IsFailure)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(postsTask.Result.Error);
        }

        var posts = postsTask.Result.Value.Posts
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

        var enrichedPosts = await EnrichPostsWithCachedStatsAsync(
            request.UserId,
            request.SocialMediaId,
            posts,
            cancellationToken);

        var dashboardPosts = CreateDashboardPosts(enrichedPosts);
        SocialPlatformAccountInsightsResponse? accountInsights = null;

        if (accountInsightsTask != null)
        {
            var accountInsightsResult = await accountInsightsTask;
            if (accountInsightsResult.IsSuccess)
            {
                accountInsights = new SocialPlatformAccountInsightsResponse(
                    AccountId: accountInsightsResult.Value.Id,
                    AccountName: accountInsightsResult.Value.Name,
                    Username: accountInsightsResult.Value.Username,
                    Followers: accountInsightsResult.Value.Followers,
                    Following: accountInsightsResult.Value.Following,
                    MediaCount: accountInsightsResult.Value.MediaCount,
                    Metadata: accountInsightsResult.Value.ProfilePictureUrl == null
                        ? null
                        : new Dictionary<string, string> { ["profilePictureUrl"] = accountInsightsResult.Value.ProfilePictureUrl });
            }
        }

        return Result.Success(CreateSummaryResponse(
            request.SocialMediaId,
            InstagramType,
            postsTask.Result.Value.NextCursor,
            postsTask.Result.Value.HasMore,
            accountInsights,
            dashboardPosts));
    }

    private async Task<Result<SocialPlatformDashboardSummaryResponse>> BuildThreadsSummaryAsync(
        GetSocialMediaDashboardSummaryQuery request,
        JsonDocument metadata,
        int postLimit,
        CancellationToken cancellationToken)
    {
        var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(
                new Error("Threads.InvalidToken", "Access token not found in social media metadata."));
        }

        var postsTask = _threadsContentService.GetPostsAsync(
            new ThreadsPostListRequest(
                AccessToken: accessToken,
                Limit: postLimit,
                Cursor: null),
            cancellationToken);

        var accountInsightsTask = _threadsContentService.GetAccountInsightsAsync(
            new ThreadsAccountInsightsRequest(accessToken),
            cancellationToken);

        await postsTask;

        if (postsTask.Result.IsFailure)
        {
            return Result.Failure<SocialPlatformDashboardSummaryResponse>(postsTask.Result.Error);
        }

        var rawPosts = postsTask.Result.Value.Posts;
        var cachedMetrics = await _postMetricSnapshotRepository.GetLatestByPlatformPostIdsAsync(
            request.UserId,
            request.SocialMediaId,
            rawPosts.Where(post => !string.IsNullOrWhiteSpace(post.Id)).Select(post => post.Id).ToArray(),
            cancellationToken);

        var cachedStatsLookup = SocialPlatformPostMetricSnapshotMapper.ToStatsLookup(cachedMetrics);

        var liveInsightsTasks = rawPosts
            .Where(post => !string.IsNullOrWhiteSpace(post.Id) && !cachedStatsLookup.ContainsKey(post.Id))
            .ToDictionary(
                post => post.Id,
                post => _threadsContentService.GetPostInsightsAsync(
                    new ThreadsPostInsightsRequest(accessToken, post.Id),
                    cancellationToken),
                StringComparer.Ordinal);

        await Task.WhenAll(liveInsightsTasks.Values);

        var posts = rawPosts
            .Select(post =>
            {
                liveInsightsTasks.TryGetValue(post.Id, out var insightsTask);
                var liveInsights = insightsTask is { Result.IsSuccess: true } ? insightsTask.Result.Value : null;
                cachedStatsLookup.TryGetValue(post.Id, out var cachedStats);

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
                    Stats: MergeStats(
                        liveInsights == null
                            ? null
                            : new SocialPlatformPostStatsResponse(
                                Views: liveInsights.Views,
                                Reach: liveInsights.Views,
                                Impressions: liveInsights.Views,
                                Likes: liveInsights.Likes,
                                Comments: liveInsights.Replies,
                                Replies: null,
                                Shares: liveInsights.Shares,
                                Reposts: liveInsights.Reposts,
                                Quotes: liveInsights.Quotes,
                                TotalInteractions: (liveInsights.Likes ?? 0) +
                                                   (liveInsights.Replies ?? 0) +
                                                   (liveInsights.Shares ?? 0) +
                                                   (liveInsights.Reposts ?? 0) +
                                                   (liveInsights.Quotes ?? 0),
                                MetricBreakdown: new Dictionary<string, long>
                                {
                                    ["views"] = liveInsights.Views ?? 0,
                                    ["likes"] = liveInsights.Likes ?? 0,
                                    ["replies"] = liveInsights.Replies ?? 0,
                                    ["reposts"] = liveInsights.Reposts ?? 0,
                                    ["quotes"] = liveInsights.Quotes ?? 0,
                                    ["shares"] = liveInsights.Shares ?? 0
                                }),
                        cachedStats));
            })
            .ToList();

        var dashboardPosts = CreateDashboardPosts(posts);
        var accountInsightsResult = await accountInsightsTask;
        var fallbackProfilePictureUrl = rawPosts.Select(post => post.ProfilePictureUrl).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var fallbackUsername = rawPosts.Select(post => post.Username).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        SocialPlatformAccountInsightsResponse? accountInsights = null;
        if (accountInsightsResult.IsSuccess)
        {
            var profilePictureUrl = accountInsightsResult.Value.ProfilePictureUrl ?? fallbackProfilePictureUrl;
            accountInsights = new SocialPlatformAccountInsightsResponse(
                AccountId: accountInsightsResult.Value.Id,
                AccountName: accountInsightsResult.Value.Name,
                Username: accountInsightsResult.Value.Username ?? fallbackUsername,
                Followers: accountInsightsResult.Value.Followers,
                Following: null,
                MediaCount: null,
                Metadata: profilePictureUrl == null && accountInsightsResult.Value.Biography == null
                    ? null
                    : new Dictionary<string, string>
                    {
                        ["profilePictureUrl"] = profilePictureUrl ?? string.Empty,
                        ["bio"] = accountInsightsResult.Value.Biography ?? string.Empty
                    });
        }
        else if (!string.IsNullOrWhiteSpace(fallbackProfilePictureUrl) || !string.IsNullOrWhiteSpace(fallbackUsername))
        {
            accountInsights = new SocialPlatformAccountInsightsResponse(
                AccountId: null,
                AccountName: null,
                Username: fallbackUsername,
                Followers: null,
                Following: null,
                MediaCount: null,
                Metadata: string.IsNullOrWhiteSpace(fallbackProfilePictureUrl)
                    ? null
                    : new Dictionary<string, string> { ["profilePictureUrl"] = fallbackProfilePictureUrl });
        }

        return Result.Success(CreateSummaryResponse(
            request.SocialMediaId,
            ThreadsType,
            postsTask.Result.Value.NextCursor,
            postsTask.Result.Value.HasMore,
            accountInsights,
            dashboardPosts));
    }

    private async Task<IReadOnlyList<SocialPlatformPostSummaryResponse>> EnrichPostsWithCachedStatsAsync(
        Guid userId,
        Guid socialMediaId,
        IReadOnlyList<SocialPlatformPostSummaryResponse> posts,
        CancellationToken cancellationToken)
    {
        var postIds = posts
            .Where(post => !string.IsNullOrWhiteSpace(post.PlatformPostId))
            .Select(post => post.PlatformPostId)
            .ToArray();

        if (postIds.Length == 0)
        {
            return posts;
        }

        var metrics = await _postMetricSnapshotRepository.GetLatestByPlatformPostIdsAsync(
            userId,
            socialMediaId,
            postIds,
            cancellationToken);

        var statsLookup = SocialPlatformPostMetricSnapshotMapper.ToStatsLookup(metrics);

        return posts
            .Select(post => post with
            {
                Stats = MergeStats(
                    post.Stats,
                    statsLookup.TryGetValue(post.PlatformPostId, out var cachedStats)
                        ? cachedStats
                        : null)
            })
            .ToList();
    }

    private static IReadOnlyList<SocialPlatformDashboardPostResponse> CreateDashboardPosts(
        IReadOnlyList<SocialPlatformPostSummaryResponse> posts)
    {
        return posts
            .Select(post => new SocialPlatformDashboardPostResponse(
                Post: post,
                Analysis: post.Stats == null ? null : SocialPlatformPostAnalysisFactory.Create(post.Stats)))
            .ToList();
    }

    private static SocialPlatformDashboardSummaryResponse CreateSummaryResponse(
        Guid socialMediaId,
        string platform,
        string? nextCursor,
        bool hasMorePosts,
        SocialPlatformAccountInsightsResponse? accountInsights,
        IReadOnlyList<SocialPlatformDashboardPostResponse> posts)
    {
        var latestPost = posts.FirstOrDefault();

        return new SocialPlatformDashboardSummaryResponse(
            SocialMediaId: socialMediaId,
            Platform: platform,
            FetchedPostCount: posts.Count,
            HasMorePosts: hasMorePosts,
            NextCursor: nextCursor,
            LatestPublishedPostId: latestPost?.Post.PlatformPostId,
            LatestPublishedAt: latestPost?.Post.PublishedAt,
            AggregatedStats: BuildAggregatedStats(posts),
            LatestAnalysis: latestPost?.Analysis,
            AccountInsights: accountInsights,
            Posts: posts);
    }

    private static SocialPlatformPostStatsResponse BuildAggregatedStats(
        IReadOnlyList<SocialPlatformDashboardPostResponse> posts)
    {
        var sourceStats = posts
            .Select(item => item.Post.Stats)
            .Where(item => item != null)
            .ToList();

        if (sourceStats.Count == 0)
        {
            return new SocialPlatformPostStatsResponse(
                Views: 0,
                Reach: 0,
                Impressions: 0,
                Likes: 0,
                Comments: 0,
                Replies: 0,
                Shares: 0,
                Reposts: 0,
                Quotes: 0,
                TotalInteractions: 0,
                Saves: 0);
        }

        var hasSaves = sourceStats.Any(item => item!.Saves.HasValue);

        return new SocialPlatformPostStatsResponse(
            Views: sourceStats.Sum(item => item!.Views ?? 0),
            Reach: sourceStats.Sum(item => item!.Reach ?? 0),
            Impressions: sourceStats.Sum(item => item!.Impressions ?? 0),
            Likes: sourceStats.Sum(item => item!.Likes ?? 0),
            Comments: sourceStats.Sum(item => item!.Comments ?? 0),
            Replies: sourceStats.Sum(item => item!.Replies ?? 0),
            Shares: sourceStats.Sum(item => item!.Shares ?? 0),
            Reposts: sourceStats.Sum(item => item!.Reposts ?? 0),
            Quotes: sourceStats.Sum(item => item!.Quotes ?? 0),
            TotalInteractions: sourceStats.Sum(item => item!.TotalInteractions),
            Saves: hasSaves ? sourceStats.Sum(item => item!.Saves ?? 0) : null);
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
