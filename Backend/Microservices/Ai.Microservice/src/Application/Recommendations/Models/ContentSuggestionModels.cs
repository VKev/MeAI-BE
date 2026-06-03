using Application.Abstractions.Rag;

namespace Application.Recommendations.Models;

public sealed record ContentSuggestionRequest(
    string? Instruction = null,
    string? Style = null,
    string? MediaType = null,
    Guid? WorkspaceId = null,
    int? TopK = null,
    int? MaxRagPosts = null,
    bool? RefreshIndex = null);

public sealed record ContentSuggestionTaskResponse(
    Guid CorrelationId,
    string Status,
    Guid SocialMediaId,
    Guid UserId,
    Guid? WorkspaceId,
    string Style,
    string MediaType,
    string? Instruction,
    DateTime CreatedAt,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record ContentSuggestionResponse(
    Guid CorrelationId,
    Guid SocialMediaId,
    Guid UserId,
    Guid? WorkspaceId,
    string Platform,
    string Style,
    string MediaType,
    string UserPrompt,
    string RecommendationSummary,
    string DocumentIdPrefix,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<WebSource>? WebSources = null,
    IReadOnlyList<ContentSuggestionReference>? References = null,
    IReadOnlyList<ContentSuggestionRetrievalError>? RetrievalErrors = null);

public sealed record ContentSuggestionReference(
    string DocumentId,
    string? PostId,
    string Source,
    string? Caption,
    double Score);

public sealed record ContentSuggestionRetrievalError(
    string Source,
    string Error);
