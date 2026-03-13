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
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";

    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly ITikTokContentService _tikTokContentService;
    private readonly IThreadsContentService _threadsContentService;
    private readonly IPostAnalyticsSnapshotRepository _postAnalyticsSnapshotRepository;

    public GetSocialMediaPlatformPostAnalyticsQueryHandler(
        IUserSocialMediaService userSocialMediaService,
        ITikTokContentService tikTokContentService,
        IThreadsContentService threadsContentService,
        IPostAnalyticsSnapshotRepository postAnalyticsSnapshotRepository)
    {
        _userSocialMediaService = userSocialMediaService;
        _tikTokContentService = tikTokContentService;
        _threadsContentService = threadsContentService;
        _postAnalyticsSnapshotRepository = postAnalyticsSnapshotRepository;
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
            var cachedSnapshot = await _postAnalyticsSnapshotRepository.GetLatestAsync(
                request.UserId,
                request.SocialMediaId,
                request.PlatformPostId,
                cancellationToken);

            var cachedResponse = SocialPlatformAnalyticsSnapshotMapper.ToResponse(cachedSnapshot);
            if (cachedResponse != null)
            {
                return Result.Success(cachedResponse);
            }
        }

        using var metadata = SocialMediaMetadataHelper.Parse(socialMedia.MetadataJson);

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

            await UpsertSnapshotAsync(request, response, cancellationToken);

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
            var stats = new SocialPlatformPostStatsResponse(
                Views: insights.Views,
                Likes: insights.Likes,
                Comments: null,
                Replies: insights.Replies,
                Shares: insights.Shares,
                Reposts: insights.Reposts,
                Quotes: insights.Quotes,
                TotalInteractions: (insights.Likes ?? 0) +
                                   (insights.Replies ?? 0) +
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

            await UpsertSnapshotAsync(request, response, cancellationToken);

            return Result.Success(response);
        }

        return Result.Failure<SocialPlatformPostAnalyticsResponse>(
            new Error("SocialMedia.UnsupportedPlatform", "Only TikTok and Threads social media accounts are supported."));
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

    private async Task UpsertSnapshotAsync(
        GetSocialMediaPlatformPostAnalyticsQuery request,
        SocialPlatformPostAnalyticsResponse response,
        CancellationToken cancellationToken)
    {
        var snapshot = await _postAnalyticsSnapshotRepository.GetLatestForUpdateAsync(
            request.UserId,
            request.SocialMediaId,
            request.PlatformPostId,
            cancellationToken);

        if (snapshot == null)
        {
            snapshot = SocialPlatformAnalyticsSnapshotMapper.Create(
                request.UserId,
                request.SocialMediaId,
                response.Platform,
                request.PlatformPostId,
                response);

            await _postAnalyticsSnapshotRepository.AddAsync(snapshot, cancellationToken);
        }
        else
        {
            SocialPlatformAnalyticsSnapshotMapper.Apply(snapshot, response);
            _postAnalyticsSnapshotRepository.Update(snapshot);
        }

        await _postAnalyticsSnapshotRepository.SaveChangesAsync(cancellationToken);
    }
}
