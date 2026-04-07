namespace Application.Posts.Models;

public sealed record GenerateSocialMediaCaptionsResponse(
    IReadOnlyList<SocialMediaCaptionsByPostResponse> SocialMedia);

public sealed record SocialMediaCaptionsByPostResponse(
    Guid PostId,
    string SocialMediaType,
    IReadOnlyList<Guid> ResourceList,
    IReadOnlyList<GeneratedCaptionResponse> Captions);

public sealed record GeneratedCaptionResponse(
    string Caption,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> TrendingHashtags,
    string? CallToAction);
