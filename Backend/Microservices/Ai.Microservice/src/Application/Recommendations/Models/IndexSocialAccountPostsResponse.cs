namespace Application.Recommendations.Models;

public sealed record IndexSocialAccountIngestFailure(
    string DocumentId,
    string? Error);

public sealed record IndexSocialAccountIngestFailureBatch(
    Guid SocialMediaId,
    string Platform,
    string DocumentIdPrefix,
    IReadOnlyList<IndexSocialAccountIngestFailure> FailedDocuments);

public sealed record IndexSocialAccountPostReadItem(
    string PlatformPostId,
    string Title,
    string? TextPreview,
    string? MediaType,
    string? Permalink,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> DocumentKinds);

public sealed record IndexSocialAccountReadBatch(
    Guid SocialMediaId,
    string Platform,
    string DocumentIdPrefix,
    IReadOnlyList<IndexSocialAccountPostReadItem> Posts);

public sealed record IndexSocialAccountPostsResponse(
    Guid SocialMediaId,
    string Platform,
    string DocumentIdPrefix,
    int TotalPostsScanned,
    int NewPosts,
    int UpdatedPosts,
    int UnchangedPosts,
    int QueuedTextDocuments,
    int QueuedImageDocuments,
    int QueuedVideoDocuments = 0,
    /// <summary>
    /// 1 if the page-profile (About / category / website / bio) was queued for ingest
    /// this run, 0 if the profile was unchanged or unavailable.
    /// </summary>
    int QueuedProfileDocuments = 0,
    /// <summary>
    /// Per-document RAG ingest failures. Non-empty means indexing continued with the
    /// documents that did succeed, but the caller should surface the partial failure.
    /// </summary>
    IReadOnlyList<IndexSocialAccountIngestFailure>? FailedIngestDocuments = null);
