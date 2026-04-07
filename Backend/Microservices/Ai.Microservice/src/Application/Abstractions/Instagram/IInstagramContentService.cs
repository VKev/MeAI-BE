using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Instagram;

public interface IInstagramContentService
{
    Task<Result<InstagramPostPageResult>> GetPostsAsync(
        InstagramPostListRequest request,
        CancellationToken cancellationToken);

    Task<Result<InstagramPostDetails>> GetPostAsync(
        InstagramPostDetailsRequest request,
        CancellationToken cancellationToken);

    Task<Result<InstagramPostInsights>> GetPostInsightsAsync(
        InstagramPostInsightsRequest request,
        CancellationToken cancellationToken);

    Task<Result<InstagramAccountInsights>> GetAccountInsightsAsync(
        InstagramAccountInsightsRequest request,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<InstagramCommentItem>>> GetPostCommentsAsync(
        InstagramPostCommentsRequest request,
        CancellationToken cancellationToken);
}

public sealed record InstagramPostListRequest(
    string AccessToken,
    string InstagramUserId,
    int? Limit = null,
    string? Cursor = null);

public sealed record InstagramPostDetailsRequest(
    string AccessToken,
    string PostId);

public sealed record InstagramPostInsightsRequest(
    string AccessToken,
    string PostId);

public sealed record InstagramAccountInsightsRequest(
    string AccessToken,
    string InstagramUserId);

public sealed record InstagramPostCommentsRequest(
    string AccessToken,
    string PostId,
    int? Limit = null);

public sealed record InstagramPostPageResult(
    IReadOnlyList<InstagramPostDetails> Posts,
    string? NextCursor,
    bool HasMore);

public sealed record InstagramPostDetails(
    string Id,
    string? Caption,
    string? MediaType,
    string? MediaProductType,
    string? MediaUrl,
    string? ThumbnailUrl,
    string? Permalink,
    string? Timestamp,
    string? Username,
    long? LikeCount,
    long? CommentCount);

public sealed record InstagramPostInsights(
    long? Views,
    long? Reach,
    long? Impressions,
    long? Saved,
    long? Shares);

public sealed record InstagramAccountInsights(
    string Id,
    string? Name,
    string? Username,
    long? Followers,
    long? Following,
    long? MediaCount,
    string? ProfilePictureUrl);

public sealed record InstagramCommentItem(
    string Id,
    string? Text,
    string? Username,
    string? Timestamp,
    long? LikeCount,
    long? RepliesCount,
    string? Permalink);