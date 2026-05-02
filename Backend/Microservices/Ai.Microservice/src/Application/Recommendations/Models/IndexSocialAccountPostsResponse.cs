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
    int QueuedImageDocuments);
