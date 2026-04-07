using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Threads;

public interface IThreadsContentService
{
    Task<Result<ThreadsPostPageResult>> GetPostsAsync(
        ThreadsPostListRequest request,
        CancellationToken cancellationToken);

    Task<Result<ThreadsPostDetails>> GetPostAsync(
        ThreadsPostDetailsRequest request,
        CancellationToken cancellationToken);

    Task<Result<ThreadsPostInsights>> GetPostInsightsAsync(
        ThreadsPostInsightsRequest request,
        CancellationToken cancellationToken);

    Task<Result<ThreadsAccountInsights>> GetAccountInsightsAsync(
        ThreadsAccountInsightsRequest request,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ThreadsReplyItem>>> GetPostRepliesAsync(
        ThreadsPostRepliesRequest request,
        CancellationToken cancellationToken);
}

public sealed record ThreadsPostListRequest(
    string AccessToken,
    int? Limit = null,
    string? Cursor = null);

public sealed record ThreadsPostDetailsRequest(
    string AccessToken,
    string PostId);

public sealed record ThreadsPostInsightsRequest(
    string AccessToken,
    string PostId);

public sealed record ThreadsAccountInsightsRequest(
    string AccessToken);

public sealed record ThreadsPostRepliesRequest(
    string AccessToken,
    string PostId,
    int? Limit = null);

public sealed record ThreadsPostPageResult(
    IReadOnlyList<ThreadsPostDetails> Posts,
    string? NextCursor,
    bool HasMore);

public sealed record ThreadsPostDetails(
    string Id,
    string? MediaProductType,
    string? MediaType,
    string? MediaUrl,
    string? GifUrl,
    string? Permalink,
    string? Username,
    string? Text,
    string? Timestamp,
    string? Shortcode,
    string? ThumbnailUrl,
    bool? IsQuotePost,
    bool? HasReplies,
    string? AltText,
    string? LinkAttachmentUrl,
    string? TopicTag,
    string? ProfilePictureUrl);

public sealed record ThreadsPostInsights(
    long? Views,
    long? Likes,
    long? Replies,
    long? Reposts,
    long? Quotes,
    long? Shares);

public sealed record ThreadsAccountInsights(
    string Id,
    string? Username,
    string? Name,
    string? Biography,
    string? ProfilePictureUrl,
    long? Followers);

public sealed record ThreadsReplyItem(
    string Id,
    string? Text,
    string? Username,
    string? Timestamp,
    long? LikeCount,
    long? ReplyCount,
    string? Permalink);
