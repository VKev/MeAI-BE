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
    Guid WorkspaceId);

public sealed record AgentChatCompletionResult(
    string Content,
    string? Model,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<AgentActionResponse>? Actions = null);
