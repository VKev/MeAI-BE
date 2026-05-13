using Application.Abstractions.Rag;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Automation;

public interface IAgenticRuntimeContentService
{
    Task<Result<AgenticRuntimePostDraft>> GeneratePostDraftAsync(
        AgenticRuntimeContentRequest request,
        CancellationToken cancellationToken);
}

public sealed record AgenticRuntimeContentRequest(
    Guid ScheduleId,
    string? ScheduleName,
    string? AgentPrompt,
    string? PlatformPreference,
    int? MaxContentLength,
    AgentWebSearchResponse Search,
    Guid? UserId = null,
    Guid? WorkspaceId = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null,
    Guid? GroundingSocialMediaId = null,
    string? GroundingPlatform = null,
    string? RecommendationQuery = null,
    string? RecommendationSummary = null,
    string? RecommendationPageProfile = null,
    IReadOnlyList<WebSource>? RecommendationWebSources = null,
    string? RagFallbackReason = null);

public sealed record AgenticRuntimePostDraft(
    string? Title,
    string Content,
    string? Hashtag,
    string PostType,
    IReadOnlyList<Guid>? ResourceIds = null);
