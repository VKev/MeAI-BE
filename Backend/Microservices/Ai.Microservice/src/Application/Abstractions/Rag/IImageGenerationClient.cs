namespace Application.Abstractions.Rag;

public interface IImageGenerationClient
{
    /// <summary>
    /// Generate an image with optional reference images for visual style.
    /// Returns a data URL (e.g. data:image/png;base64,...) suitable for handing to
    /// IUserResourceService.CreateResourcesFromUrlsAsync, plus the underlying mime type.
    /// </summary>
    Task<ImageGenerationResult> GenerateImageAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed record ImageGenerationRequest(
    string Prompt,
    IReadOnlyList<string>? ReferenceImageUrls,
    string? SystemPrompt = null);

public sealed record ImageGenerationResult(
    string DataUrl,
    string MimeType,
    int? PromptTokens,
    int? CompletionTokens,
    decimal? CostUsd);
