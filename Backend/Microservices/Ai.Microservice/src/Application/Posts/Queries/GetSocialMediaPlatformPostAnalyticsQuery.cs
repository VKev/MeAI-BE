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

        if (!request.Refresh)
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
            var stats = new SocialPlatformPostStatsResponse(
                Views: postDetails.ViewCount,
                Likes: postDetails.ReactionCount,
                Comments: postDetails.CommentCount,
                Replies: null,
                Shares: postDetails.ShareCount,
                Reposts: null,
                Quotes: null,
                TotalInteractions: (postDetails.ReactionCount ?? 0) +
                                   (postDetails.CommentCount ?? 0) +
                                   (postDetails.ShareCount ?? 0),
                ReactionBreakdown: postDetails.ReactionBreakdown);

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
                RetrievedAt: DateTimeOffset.UtcNow);

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

            if (videoResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(videoResult.Error);
            }

            var video = videoResult.Value;
            var stats = new SocialPlatformPostStatsResponse(
                Views: video.ViewCount,
                Likes: video.LikeCount,
                Comments: video.CommentCount,
                Replies: null,
                Shares: video.ShareCount,
                Reposts: null,
                Quotes: null,
                TotalInteractions: (video.ViewCount is null && video.LikeCount is null && video.CommentCount is null && video.ShareCount is null)
                    ? 0
                    : (video.LikeCount ?? 0) + (video.CommentCount ?? 0) + (video.ShareCount ?? 0));

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
                RetrievedAt: DateTimeOffset.UtcNow);

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
                new InstagramPostInsightsRequest(accessToken, request.PlatformPostId),
                cancellationToken);

            if (insightsResult.IsFailure)
            {
                return Result.Failure<SocialPlatformPostAnalyticsResponse>(insightsResult.Error);
            }

            var postDetails = postResult.Value;
            var insights = insightsResult.Value;
            var stats = new SocialPlatformPostStatsResponse(
                Views: insights.Views ?? insights.Reach,
                Likes: postDetails.LikeCount,
                Comments: postDetails.CommentCount,
                Replies: null,
                Shares: insights.Shares,
                Reposts: null,
                Quotes: null,
                TotalInteractions: (postDetails.LikeCount ?? 0) +
                                   (postDetails.CommentCount ?? 0) +
                                   (insights.Shares ?? 0));

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
                RetrievedAt: DateTimeOffset.UtcNow);

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
            var comments = insights.Replies;
            var stats = new SocialPlatformPostStatsResponse(
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
                RetrievedAt: DateTimeOffset.UtcNow);

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
