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
    N8nWebSearchResponse Search);

public sealed record AgenticRuntimePostDraft(
    string? Title,
    string Content,
    string? Hashtag,
    string PostType);
