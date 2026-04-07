using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.TikTok;

public interface ITikTokContentService
{
    Task<Result<TikTokVideoPageResult>> GetVideosAsync(
        TikTokVideoListRequest request,
        CancellationToken cancellationToken);

    Task<Result<TikTokVideoDetails>> GetVideoAsync(
        TikTokVideoDetailsRequest request,
        CancellationToken cancellationToken);

    Task<Result<TikTokAccountInsights>> GetAccountInsightsAsync(
        TikTokAccountInsightsRequest request,
        CancellationToken cancellationToken);
}

public sealed record TikTokVideoListRequest(
    string AccessToken,
    long? Cursor = null,
    int? MaxCount = null);

public sealed record TikTokVideoDetailsRequest(
    string AccessToken,
    string VideoId);

public sealed record TikTokAccountInsightsRequest(
    string AccessToken);

public sealed record TikTokVideoPageResult(
    IReadOnlyList<TikTokVideoDetails> Videos,
    long? Cursor,
    bool HasMore);

public sealed record TikTokVideoDetails(
    string Id,
    string? Title,
    string? VideoDescription,
    string? CoverImageUrl,
    string? ShareUrl,
    string? EmbedLink,
    int? Duration,
    long? CreateTime,
    long? ViewCount,
    long? LikeCount,
    long? CommentCount,
    long? ShareCount);

public sealed record TikTokAccountInsights(
    string? OpenId,
    string? DisplayName,
    string? AvatarUrl,
    string? BioDescription,
    long? FollowerCount,
    long? FollowingCount,
    long? LikesCount,
    long? VideoCount);
