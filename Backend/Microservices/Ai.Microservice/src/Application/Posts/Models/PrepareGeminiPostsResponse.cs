namespace Application.Posts.Models;

public sealed record PrepareGeminiPostsResponse(
    Guid PostBuilderId,
    Guid? WorkspaceId,
    string PostType,
    IReadOnlyList<PreparedSocialMediaDraftGroupResponse> SocialMedia,
    IReadOnlyList<Guid> ResourceIds);

public sealed record PreparedSocialMediaDraftGroupResponse(
    Guid? SocialMediaId,
    string Type,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<PreparedDraftPostResponse> Drafts);

public sealed record PreparedDraftPostResponse(
    Guid PostId,
    string Status,
    string PostType,
    string Caption,
    string? Title,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> TrendingHashtags,
    string? CallToAction);
