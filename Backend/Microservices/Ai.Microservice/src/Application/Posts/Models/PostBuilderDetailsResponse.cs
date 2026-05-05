namespace Application.Posts.Models;

public sealed record PostBuilderDetailsResponse(
    Guid Id,
    Guid? WorkspaceId,
    string? OriginKind,
    string? Type,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<PostMediaResponse> Resources,
    IReadOnlyList<PostBuilderSocialMediaGroupResponse> SocialMedia,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record PostBuilderSocialMediaGroupResponse(
    Guid? SocialMediaId,
    string Platform,
    string? Type,
    IReadOnlyList<PostResponse> Posts);

public sealed record PostBuilderSummaryResponse(
    Guid Id,
    Guid? WorkspaceId,
    string? OriginKind,
    string? Type,
    int PostCount,
    int PublishedCount,
    IReadOnlyList<string> Platforms,
    string? ThumbnailUrl,
    string? FirstPostSnippet,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
