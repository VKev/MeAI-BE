namespace Application.Posts.Models;

public sealed record PublishPostResponse(
    Guid PostId,
    string Status,
    IReadOnlyList<PublishPostDestinationResult> Results);

public sealed record PublishPostDestinationResult(
    Guid SocialMediaId,
    string SocialMediaType,
    string PageId,
    string ExternalPostId);
