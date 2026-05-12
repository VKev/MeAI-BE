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
    long? ImpressionCount = null,
    string? VideoSourceUrl = null);

public sealed record FacebookPageInsights(
    string PageId,
    string? Name,
    long? Followers,
    long? Fans,
    /// <summary>
    /// One-line page tagline shown under the name. Often empty.
    /// </summary>
    string? About = null,
    /// <summary>
    /// The full multi-line "Giới thiệu" / "About" text from the page profile.
    /// Used by the recommendation/draft RAG to ground generated content in what
    /// the page is actually about.
    /// </summary>
    string? Description = null,
    /// <summary>
    /// Comma-joined category list (e.g. "Digital content creator, Education").
    /// </summary>
    string? Category = null,
    string? Website = null,
    string? Email = null,
    string? Phone = null,
    /// <summary>
    /// Comma-joined location string (street, city, country).
    /// </summary>
    string? Location = null,
    /// <summary>
    /// Short bio (some page types only).
    /// </summary>
    string? Bio = null,
    /// <summary>
    /// Mission / company-overview / founded — joined free-form for richness when present.
    /// </summary>
    string? CompanyOverview = null,
    /// <summary>
    /// Page profile picture URL (often the brand logo). Used by the RAG ingest
    /// to describe the visual identity and to register a visual reference.
    /// </summary>
    string? PageProfilePictureUrl = null);

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
