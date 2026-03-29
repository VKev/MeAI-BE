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
    long? ReactionCount,
    long? CommentCount,
    long? ShareCount);
