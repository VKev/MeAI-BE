using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Gemini;

public interface IGeminiContentModerationService
{
    Task<Result<ContentModerationResult>> CheckSensitiveContentAsync(
        ContentModerationRequest request,
        CancellationToken cancellationToken);
}

public sealed record ContentModerationRequest(
    string Text,
    IReadOnlyList<ContentModerationResource>? MediaResources = null,
    string? PreferredModel = null);

public sealed record ContentModerationResource(
    string FileUri,
    string MimeType);

public sealed record ContentModerationResult(
    bool IsSensitive,
    string? Category,
    string? Reason,
    double ConfidenceScore);
