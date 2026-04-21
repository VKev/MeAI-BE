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

public sealed record GetSocialMediaPlatformPostAnalyticsQuery(
    Guid UserId,
    Guid SocialMediaId,
    string PlatformPostId,
    bool Refresh = false) : IRequest<Result<SocialPlatformPostAnalyticsResponse>>;

public sealed class GetSocialMediaPlatformPostAnalyticsQueryHandler
    : IRequestHandler<GetSocialMediaPlatformPostAnalyticsQuery, Result<SocialPlatformPostAnalyticsResponse>>
{
    private const int DefaultCommentSampleSize = 25;
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

    public GetSocialMediaPlatformPostAnalyticsQueryHandler(
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

    public async Task<Result<SocialPlatformPostAnalyticsResponse>> Handle(
        GetSocialMediaPlatformPostAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<SocialPlatformPostAnalyticsResponse>(socialMediaResult.Error);
        }

        var socialMedia = socialMediaResult.Value.FirstOrDefault();
        if (socialMedia == null)
        {
            return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        var isTikTok = string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase);
        var isFacebook = string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase);
        // Threads must bypass the snapshot cache because PostMetricSnapshotMapper only
        // persists numeric metrics + the post payload — CommentSamples aren't stored.
        // Serving Threads from cache would drop the reply detail the FE needs to render.
        var isThreads = string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase);

        if (!request.Refresh && !isTikTok && !isFacebook && !isThreads)
        {
            var cachedMetric = await _postMetricSnapshotRepository.GetLatestAsync(
                request.UserId,
                request.SocialMediaId,
                request.PlatformPostId,
                cancellationToken);

            var cachedResponse = SocialPlatformPostMetricSnapshotMapper.ToAnalyticsResponse(cachedMetric);
            if (cachedResponse != null)
            {
                return Result.Success(cachedResponse);
            }
        }

        using var metadata = SocialMediaMetadataHelper.Parse(socialMedia.MetadataJson);

        if (string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var userAccessToken = SocialMediaMetadataHelper.GetString(metadata, "user_access_token")
                                  ?? SocialMediaMetadataHelper.GetString(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(userAccessToken))
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                    new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
            }

            var postResult = await _facebookContentService.GetPostAsync(
                new FacebookPostDetailsRequest(
                    UserAccessToken: userAccessToken,
                    PostId: request.PlatformPostId,
                    PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                    PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token")),
                cancellationToken);

            if (postResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(postResult.Error);
            }

            var postDetails = postResult.Value;
            var pageInsightsTask = _facebookContentService.GetPageInsightsAsync(
                new FacebookPageInsightsRequest(
                    UserAccessToken: userAccessToken,
                    PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                    PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token")),
                cancellationToken);

            var commentsTask = _facebookContentService.GetPostCommentsAsync(
                new FacebookPostCommentsRequest(
                    UserAccessToken: userAccessToken,
                    PostId: request.PlatformPostId,
                    PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                    PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token"),
                    Limit: DefaultCommentSampleSize),
                cancellationToken);

            await Task.WhenAll(pageInsightsTask, commentsTask);

            var stats = new SocialPlatformPostStatsResponse(
                Views: postDetails.ViewCount,
                Reach: postDetails.ReachCount,
                Impressions: postDetails.ImpressionCount,
                Likes: postDetails.ReactionCount,
                Comments: postDetails.CommentCount,
                Replies: null,
                Shares: postDetails.ShareCount,
                Reposts: null,
                Quotes: null,
                TotalInteractions: (postDetails.ReactionCount ?? 0) +
                                   (postDetails.CommentCount ?? 0) +
                                   (postDetails.ShareCount ?? 0),
                ReactionBreakdown: postDetails.ReactionBreakdown,
                MetricBreakdown: new Dictionary<string, long>
                {
                    ["views"] = postDetails.ViewCount ?? 0,
                    ["reach"] = postDetails.ReachCount ?? 0,
                    ["impressions"] = postDetails.ImpressionCount ?? 0,
                    ["likes"] = postDetails.ReactionCount ?? 0,
                    ["comments"] = postDetails.CommentCount ?? 0,
                    ["shares"] = postDetails.ShareCount ?? 0
                });

            var post = new SocialPlatformPostSummaryResponse(
                PlatformPostId: postDetails.Id,
                Title: postDetails.AttachmentTitle,
                Text: postDetails.Message ?? postDetails.Story,
                Description: postDetails.AttachmentDescription,
                MediaType: postDetails.MediaType,
                MediaUrl: postDetails.MediaUrl ?? postDetails.FullPictureUrl,
                ThumbnailUrl: postDetails.ThumbnailUrl ?? postDetails.FullPictureUrl,
                Permalink: postDetails.PermalinkUrl,
                ShareUrl: postDetails.PermalinkUrl,
                EmbedUrl: null,
                DurationSeconds: null,
                PublishedAt: ToDateTimeOffset(postDetails.CreatedTime),
                Stats: stats);

            var response = new SocialPlatformPostAnalyticsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: FacebookType,
                PlatformPostId: request.PlatformPostId,
                Post: post,
                Stats: stats,
                Analysis: SocialPlatformPostAnalysisFactory.Create(stats),
                RetrievedAt: DateTimeOffset.UtcNow,
                AccountInsights: pageInsightsTask.Result.IsSuccess
                    ? new SocialPlatformAccountInsightsResponse(
                        AccountId: pageInsightsTask.Result.Value.PageId,
                        AccountName: pageInsightsTask.Result.Value.Name,
                        Username: null,
                        Followers: pageInsightsTask.Result.Value.Followers ?? pageInsightsTask.Result.Value.Fans,
                        Following: null,
                        MediaCount: null)
                    : null,
                CommentSamples: commentsTask.Result.IsSuccess
                    ? commentsTask.Result.Value.Select(MapComment).ToList()
                    : null);

            await UpsertMetricAsync(request, response, cancellationToken);

            return Result.Success(response);
        }

        if (string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                    new Error("TikTok.InvalidToken", "Access token not found in social media metadata."));
            }

            if (!SocialMediaMetadataHelper.HasScope(metadata, "video.list"))
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                    new Error("TikTok.InsufficientScope", "TikTok account must be reconnected with the video.list scope."));
            }

            var videoResult = await _tikTokContentService.GetVideoAsync(
                new TikTokVideoDetailsRequest(accessToken, request.PlatformPostId),
                cancellationToken);

            var accountInsightsTask = _tikTokContentService.GetAccountInsightsAsync(
                new TikTokAccountInsightsRequest(accessToken),
                cancellationToken);

            if (videoResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(videoResult.Error);
            }

            var accountInsightsResult = await accountInsightsTask;

            var video = videoResult.Value;
            var stats = new SocialPlatformPostStatsResponse(
                Views: video.ViewCount,
                Reach: video.ViewCount,
                Impressions: video.ViewCount,
                Likes: video.LikeCount,
                Comments: video.CommentCount,
                Replies: null,
                Shares: video.ShareCount,
                Reposts: null,
                Quotes: null,
                TotalInteractions: (video.ViewCount is null && video.LikeCount is null && video.CommentCount is null && video.ShareCount is null)
                    ? 0
                    : (video.LikeCount ?? 0) + (video.CommentCount ?? 0) + (video.ShareCount ?? 0),
                MetricBreakdown: new Dictionary<string, long>
                {
                    ["views"] = video.ViewCount ?? 0,
                    ["likes"] = video.LikeCount ?? 0,
                    ["comments"] = video.CommentCount ?? 0,
                    ["shares"] = video.ShareCount ?? 0
                });

            var post = new SocialPlatformPostSummaryResponse(
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
                Stats: stats);

            var response = new SocialPlatformPostAnalyticsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: TikTokType,
                PlatformPostId: request.PlatformPostId,
                Post: post,
                Stats: stats,
                Analysis: SocialPlatformPostAnalysisFactory.Create(stats),
                RetrievedAt: DateTimeOffset.UtcNow,
                AccountInsights: accountInsightsResult.IsSuccess
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
                    : null);

            await UpsertMetricAsync(request, response, cancellationToken);

            return Result.Success(response);
        }

        if (string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token")
                              ?? SocialMediaMetadataHelper.GetString(metadata, "user_access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                    new Error("Instagram.InvalidToken", "Access token not found in social media metadata."));
            }

            var postResult = await _instagramContentService.GetPostAsync(
                new InstagramPostDetailsRequest(accessToken, request.PlatformPostId),
                cancellationToken);

            if (postResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(postResult.Error);
            }

            var insightsResult = await _instagramContentService.GetPostInsightsAsync(
                new InstagramPostInsightsRequest(
                    accessToken,
                    request.PlatformPostId,
                    postResult.Value.MediaType,
                    postResult.Value.MediaProductType),
                cancellationToken);

            if (insightsResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(insightsResult.Error);
            }

            var postDetails = postResult.Value;
            var insights = insightsResult.Value;
            var instagramUserId = SocialMediaMetadataHelper.GetString(metadata, "instagram_business_account_id")
                                  ?? SocialMediaMetadataHelper.GetString(metadata, "user_id")
                                  ?? SocialMediaMetadataHelper.GetString(metadata, "id");

            Task<Result<InstagramAccountInsights>>? accountInsightsTask = null;
            if (!string.IsNullOrWhiteSpace(instagramUserId))
            {
                accountInsightsTask = _instagramContentService.GetAccountInsightsAsync(
                    new InstagramAccountInsightsRequest(accessToken, instagramUserId),
                    cancellationToken);
            }

            var commentsTask = _instagramContentService.GetPostCommentsAsync(
                new InstagramPostCommentsRequest(accessToken, request.PlatformPostId, DefaultCommentSampleSize),
                cancellationToken);

            if (accountInsightsTask != null)
            {
                await Task.WhenAll(accountInsightsTask, commentsTask);
            }
            else
            {
                await commentsTask;
            }

            var stats = new SocialPlatformPostStatsResponse(
                Views: insights.Views ?? insights.Reach,
                Reach: insights.Reach,
                Impressions: insights.Impressions,
                Likes: postDetails.LikeCount,
                Comments: postDetails.CommentCount,
                Replies: null,
                Shares: insights.Shares,
                Reposts: null,
                Quotes: null,
                TotalInteractions: (postDetails.LikeCount ?? 0) +
                                   (postDetails.CommentCount ?? 0) +
                                   (insights.Shares ?? 0),
                Saves: insights.Saved,
                MetricBreakdown: new Dictionary<string, long>
                {
                    ["reach"] = insights.Reach ?? 0,
                    ["impressions"] = insights.Impressions ?? 0,
                    ["shares"] = insights.Shares ?? 0,
                    ["saved"] = insights.Saved ?? 0
                });

            var post = new SocialPlatformPostSummaryResponse(
                PlatformPostId: postDetails.Id,
                Title: null,
                Text: postDetails.Caption,
                Description: null,
                MediaType: postDetails.MediaType,
                MediaUrl: postDetails.MediaUrl,
                ThumbnailUrl: postDetails.ThumbnailUrl,
                Permalink: postDetails.Permalink,
                ShareUrl: postDetails.Permalink,
                EmbedUrl: null,
                DurationSeconds: null,
                PublishedAt: ToDateTimeOffset(postDetails.Timestamp),
                Stats: stats);

            var response = new SocialPlatformPostAnalyticsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: InstagramType,
                PlatformPostId: request.PlatformPostId,
                Post: post,
                Stats: stats,
                Analysis: SocialPlatformPostAnalysisFactory.Create(stats),
                RetrievedAt: DateTimeOffset.UtcNow,
                AccountInsights: accountInsightsTask is { Result.IsSuccess: true }
                    ? new SocialPlatformAccountInsightsResponse(
                        AccountId: accountInsightsTask.Result.Value.Id,
                        AccountName: accountInsightsTask.Result.Value.Name,
                        Username: accountInsightsTask.Result.Value.Username,
                        Followers: accountInsightsTask.Result.Value.Followers,
                        Following: accountInsightsTask.Result.Value.Following,
                        MediaCount: accountInsightsTask.Result.Value.MediaCount,
                        Metadata: accountInsightsTask.Result.Value.ProfilePictureUrl == null
                            ? null
                            : new Dictionary<string, string> { ["profilePictureUrl"] = accountInsightsTask.Result.Value.ProfilePictureUrl })
                    : null,
                CommentSamples: commentsTask.Result.IsSuccess
                    ? commentsTask.Result.Value.Select(item => new SocialPlatformCommentResponse(
                        Id: item.Id,
                        Text: item.Text,
                        AuthorId: null,
                        AuthorName: item.Username,
                        AuthorUsername: item.Username,
                        CreatedAt: ToDateTimeOffset(item.Timestamp),
                        LikeCount: item.LikeCount,
                        ReplyCount: item.RepliesCount,
                        Permalink: item.Permalink))
                        .ToList()
                    : null);

            await UpsertMetricAsync(request, response, cancellationToken);

            return Result.Success(response);
        }

        if (string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = SocialMediaMetadataHelper.GetString(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(
                    new Error("Threads.InvalidToken", "Access token not found in social media metadata."));
            }

            var postResult = await _threadsContentService.GetPostAsync(
                new ThreadsPostDetailsRequest(accessToken, request.PlatformPostId),
                cancellationToken);

            if (postResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(postResult.Error);
            }

            var insightsResult = await _threadsContentService.GetPostInsightsAsync(
                new ThreadsPostInsightsRequest(accessToken, request.PlatformPostId),
                cancellationToken);

            if (insightsResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(insightsResult.Error);
            }

            var postDetails = postResult.Value;
            var insights = insightsResult.Value;
            var accountInsightsTask = _threadsContentService.GetAccountInsightsAsync(
                new ThreadsAccountInsightsRequest(accessToken),
                cancellationToken);

            var repliesTask = _threadsContentService.GetPostRepliesAsync(
                new ThreadsPostRepliesRequest(accessToken, request.PlatformPostId, DefaultCommentSampleSize),
                cancellationToken);

            await Task.WhenAll(accountInsightsTask, repliesTask);

            var comments = insights.Replies;
            var stats = new SocialPlatformPostStatsResponse(
                Views: insights.Views,
                Reach: insights.Views,
                Impressions: insights.Views,
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
                                   (insights.Quotes ?? 0),
                MetricBreakdown: new Dictionary<string, long>
                {
                    ["views"] = insights.Views ?? 0,
                    ["likes"] = insights.Likes ?? 0,
                    ["replies"] = insights.Replies ?? 0,
                    ["reposts"] = insights.Reposts ?? 0,
                    ["quotes"] = insights.Quotes ?? 0,
                    ["shares"] = insights.Shares ?? 0
                });

            var post = new SocialPlatformPostSummaryResponse(
                PlatformPostId: postDetails.Id,
                Title: null,
                Text: postDetails.Text,
                Description: null,
                MediaType: postDetails.MediaType,
                MediaUrl: postDetails.MediaUrl ?? postDetails.GifUrl,
                ThumbnailUrl: postDetails.ThumbnailUrl ?? postDetails.ProfilePictureUrl,
                Permalink: postDetails.Permalink,
                ShareUrl: postDetails.Permalink,
                EmbedUrl: null,
                DurationSeconds: null,
                PublishedAt: ToDateTimeOffset(postDetails.Timestamp),
                Stats: stats);

            var response = new SocialPlatformPostAnalyticsResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: ThreadsType,
                PlatformPostId: request.PlatformPostId,
                Post: post,
                Stats: stats,
                Analysis: SocialPlatformPostAnalysisFactory.Create(stats),
                RetrievedAt: DateTimeOffset.UtcNow,
                AccountInsights: accountInsightsTask.Result.IsSuccess
                    ? new SocialPlatformAccountInsightsResponse(
                        AccountId: accountInsightsTask.Result.Value.Id,
                        AccountName: accountInsightsTask.Result.Value.Name,
                        Username: accountInsightsTask.Result.Value.Username,
                        Followers: accountInsightsTask.Result.Value.Followers,
                        Following: null,
                        MediaCount: null,
                        Metadata: accountInsightsTask.Result.Value.ProfilePictureUrl == null && accountInsightsTask.Result.Value.Biography == null
                            ? null
                            : new Dictionary<string, string>
                            {
                                ["profilePictureUrl"] = accountInsightsTask.Result.Value.ProfilePictureUrl ?? string.Empty,
                                ["bio"] = accountInsightsTask.Result.Value.Biography ?? string.Empty
                            })
                    : null,
                CommentSamples: repliesTask.Result.IsSuccess
                    ? repliesTask.Result.Value.Select(item => new SocialPlatformCommentResponse(
                        Id: item.Id,
                        Text: item.Text,
                        AuthorId: null,
                        AuthorName: item.Username,
                        AuthorUsername: item.Username,
                        CreatedAt: ToDateTimeOffset(item.Timestamp),
                        LikeCount: item.LikeCount,
                        ReplyCount: item.ReplyCount,
                        Permalink: item.Permalink))
                        .ToList()
                    : null);

            await UpsertMetricAsync(request, response, cancellationToken);

            return Result.Success(response);
        }

        return Result.Failure<SocialPlatformPostAnalyticsResponse>(
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

    private static SocialPlatformCommentResponse MapComment(SocialPlatformCommentItem item)
    {
        return new SocialPlatformCommentResponse(
            Id: item.Id,
            Text: item.Text,
            AuthorId: item.AuthorId,
            AuthorName: item.AuthorName,
            AuthorUsername: item.AuthorUsername,
            CreatedAt: item.CreatedAt,
            LikeCount: item.LikeCount,
            ReplyCount: item.ReplyCount,
            Permalink: item.Permalink);
    }

    private async Task UpsertMetricAsync(
        GetSocialMediaPlatformPostAnalyticsQuery request,
        SocialPlatformPostAnalyticsResponse response,
        CancellationToken cancellationToken)
    {
        var metric = await _postMetricSnapshotRepository.GetLatestForUpdateAsync(
            request.UserId,
            request.SocialMediaId,
            request.PlatformPostId,
            cancellationToken);

        if (metric == null)
        {
            metric = SocialPlatformPostMetricSnapshotMapper.Create(
                request.UserId,
                request.SocialMediaId,
                response.Platform,
                request.PlatformPostId,
                response);

            await _postMetricSnapshotRepository.AddAsync(metric, cancellationToken);
        }
        else
        {
            SocialPlatformPostMetricSnapshotMapper.Apply(metric, response);
            _postMetricSnapshotRepository.Update(metric);
        }

        await _postMetricSnapshotRepository.SaveChangesAsync(cancellationToken);
    }
}
