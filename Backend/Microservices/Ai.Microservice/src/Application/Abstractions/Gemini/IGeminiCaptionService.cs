using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Gemini;

public interface IGeminiCaptionService
{
    Task<Result<string>> GenerateCaptionAsync(
        GeminiCaptionRequest request,
        CancellationToken cancellationToken);

    Task<Result<string>> GenerateTitleAsync(
        GeminiTitleRequest request,
        CancellationToken cancellationToken);
}

public sealed record GeminiCaptionRequest(
    IReadOnlyList<GeminiCaptionResource> Resources,
    string PostType,
    string? LanguageHint,
    string? Instruction);

public sealed record GeminiCaptionResource(
    string FileUri,
    string MimeType);

public sealed record GeminiTitleRequest(
    string Content,
    string? LanguageHint);
