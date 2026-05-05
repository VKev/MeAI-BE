namespace Application.Recommendations.Models;

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
    int QueuedProfileDocuments = 0);
