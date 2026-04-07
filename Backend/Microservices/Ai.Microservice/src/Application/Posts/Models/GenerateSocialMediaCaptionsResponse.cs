namespace Application.Posts.Models;

public sealed record GenerateSocialMediaCaptionsResponse(
    string TemplateFileName,
    string TemplateMimeType,
    IReadOnlyList<SocialMediaCaptionsByPlatformResponse> SocialMedia);

public sealed record SocialMediaCaptionsByPlatformResponse(
    string Type,
    IReadOnlyList<string> ResourceList,
    IReadOnlyList<GeneratedCaptionResponse> Captions);

public sealed record GeneratedCaptionResponse(
    string Caption,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> TrendingHashtags,
    string? CallToAction);
