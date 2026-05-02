using Application.Agents.Models;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Agents;

public interface IAgentChatService
{
    Task<Result<AgentChatCompletionResult>> GenerateReplyAsync(
        AgentChatRequest request,
        CancellationToken cancellationToken);
}

public sealed record AgentChatRequest(
    Guid UserId,
    Guid SessionId,
    Guid WorkspaceId,
    string Message,
    AgentImageOptions? ImageOptions = null,
    Guid? AssistantChatId = null);

public sealed record AgentImageOptions(
    string? Model = null,
    string? AspectRatio = null,
    string? Resolution = null,
    int? NumberOfVariances = null,
    IReadOnlyList<AgentSocialTarget>? SocialTargets = null);

public sealed record AgentSocialTarget(
    string Platform,
    string Type,
    string Ratio);

public sealed record AgentChatCompletionResult(
    string Content,
    string? Model,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<AgentActionResponse>? Actions = null,
    string? Action = null,
    string? ValidationError = null,
    string? RevisedPrompt = null,
    Guid? PostId = null,
    Guid? ChatId = null,
    Guid? CorrelationId = null,
    string? RetrievalMode = null,
    IReadOnlyList<string>? SourceUrls = null,
    IReadOnlyList<Guid>? ImportedResourceIds = null);
