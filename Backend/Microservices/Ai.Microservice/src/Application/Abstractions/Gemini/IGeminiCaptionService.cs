using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Gemini;

public interface IGeminiCaptionService
{
    Task<Result<string>> GenerateCaptionAsync(
        GeminiCaptionRequest request,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GeminiGeneratedCaption>>> GenerateSocialMediaCaptionsAsync(
        GeminiSocialMediaCaptionRequest request,
        CancellationToken cancellationToken);

    Task<Result<string>> GenerateTitleAsync(
        GeminiTitleRequest request,
        CancellationToken cancellationToken);
}

public sealed record GeminiCaptionRequest(
    IReadOnlyList<GeminiCaptionResource> Resources,
    string PostType,
    string? LanguageHint,
    string? Instruction,
    string? PreferredModel = null);

public sealed record GeminiCaptionResource(
    string FileUri,
    string MimeType);

public sealed record GeminiSocialMediaCaptionRequest(
    IReadOnlyList<GeminiCaptionResource> Resources,
    GeminiInlineCaptionResource? InlineTemplateResource,
    string Platform,
    IReadOnlyList<string> ResourceHints,
    int CaptionCount,
    string? LanguageHint,
    string? Instruction,
    string? PreferredModel = null,
    // "posts" | "reels" — lets the caption generator pick reel-appropriate tone/length
    // (short hook, CTA to watch vs. long-form evergreen for feed posts).
    string? PostType = null);

public sealed record GeminiInlineCaptionResource(
    string MimeType,
    byte[] Content);

public sealed record GeminiGeneratedCaption(
    string Caption,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> TrendingHashtags,
    string? CallToAction);

public sealed record GeminiTitleRequest(
    string Content,
    string? LanguageHint,
    string? PreferredModel = null);
