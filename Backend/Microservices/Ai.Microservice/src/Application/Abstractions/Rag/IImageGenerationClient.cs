namespace Application.Abstractions.Rag;

public interface IImageGenerationClient
{
    /// <summary>
    /// Generate an image with optional reference images for visual style.
    /// Returns a provider URL when available, or a data URL when the provider only returns
    /// inline image bytes. Both forms are suitable for IUserResourceService.CreateResourcesFromUrlsAsync.
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
    string Url,
    string MimeType,
    int? PromptTokens,
    int? CompletionTokens,
    decimal? CostUsd)
{
    public string DataUrl => Url;

    public bool IsDataUrl => Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
}
