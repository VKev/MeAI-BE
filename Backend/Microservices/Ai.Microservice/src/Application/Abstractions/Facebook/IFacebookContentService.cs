using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Facebook;

public interface IFacebookContentService
{
    Task<Result<FacebookPostPageResult>> GetPostsAsync(
        FacebookPostListRequest request,
        CancellationToken cancellationToken);

    Task<Result<FacebookPostDetails>> GetPostAsync(
        FacebookPostDetailsRequest request,
        CancellationToken cancellationToken);

    Task<Result<FacebookPageInsights>> GetPageInsightsAsync(
        FacebookPageInsightsRequest request,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SocialPlatformCommentItem>>> GetPostCommentsAsync(
        FacebookPostCommentsRequest request,
        CancellationToken cancellationToken);
}

public sealed record FacebookPostListRequest(
    string UserAccessToken,
    string? PreferredPageId = null,
    string? PreferredPageAccessToken = null,
    int? Limit = null,
    string? Cursor = null);

public sealed record FacebookPostDetailsRequest(
    string UserAccessToken,
    string PostId,
    string? PreferredPageId = null,
    string? PreferredPageAccessToken = null);

public sealed record FacebookPageInsightsRequest(
    string UserAccessToken,
    string? PreferredPageId = null,
    string? PreferredPageAccessToken = null);

public sealed record FacebookPostCommentsRequest(
    string UserAccessToken,
    string PostId,
    string? PreferredPageId = null,
    string? PreferredPageAccessToken = null,
    int? Limit = null);

public sealed record FacebookPostPageResult(
    IReadOnlyList<FacebookPostDetails> Posts,
    string? NextCursor,
    bool HasMore);

public sealed record FacebookPostDetails(
    string Id,
    string PageId,
    string? Message,
    string? Story,
    string? PermalinkUrl,
    string? CreatedTime,
    string? FullPictureUrl,
    string? MediaType,
    string? MediaUrl,
    string? ThumbnailUrl,
    string? AttachmentTitle,
    string? AttachmentDescription,
    long? ViewCount,
    long? ReactionCount,
    long? CommentCount,
    long? ShareCount,
    IReadOnlyDictionary<string, long>? ReactionBreakdown = null,
    long? ReachCount = null,
    long? ImpressionCount = null);

public sealed record FacebookPageInsights(
    string PageId,
    string? Name,
    long? Followers,
    long? Fans);

public sealed record SocialPlatformCommentItem(
    string Id,
    string? Text,
    string? AuthorId,
    string? AuthorName,
    string? AuthorUsername,
    DateTimeOffset? CreatedAt,
    long? LikeCount,
    long? ReplyCount,
    string? Permalink);
