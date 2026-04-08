namespace Application.Posts.Models;

public sealed record PostBuilderDetailsResponse(
    Guid Id,
    Guid? WorkspaceId,
    string? Type,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<PostBuilderSocialMediaGroupResponse> SocialMedia,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record PostBuilderSocialMediaGroupResponse(
    Guid? SocialMediaId,
    string Platform,
    string? Type,
    IReadOnlyList<PostResponse> Posts);
