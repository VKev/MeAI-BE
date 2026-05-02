namespace Application.Recommendations.Models;

public sealed record DraftPostTaskResponse(
    Guid CorrelationId,
    string Status,
    Guid SocialMediaId,
    Guid UserId,
    Guid? WorkspaceId,
    string UserPrompt,
    Guid? ResultPostBuilderId,
    Guid? ResultPostId,
    Guid? ResultResourceId,
    string? ResultPresignedUrl,
    string? ResultCaption,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record StartDraftPostGenerationRequest(
    string UserPrompt,
    Guid? WorkspaceId = null,
    int? TopK = null,
    int? MaxReferenceImages = null,
    int? MaxRagPosts = null);
