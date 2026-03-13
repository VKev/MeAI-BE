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
}

public sealed record TikTokVideoListRequest(
    string AccessToken,
    long? Cursor = null,
    int? MaxCount = null);

public sealed record TikTokVideoDetailsRequest(
    string AccessToken,
    string VideoId);

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
