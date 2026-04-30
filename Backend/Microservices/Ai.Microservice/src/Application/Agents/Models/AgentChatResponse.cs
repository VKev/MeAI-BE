namespace Application.Agents.Models;

public sealed record AgentChatResponse(
    Guid SessionId,
    AgentMessageResponse UserMessage,
    AgentMessageResponse AssistantMessage,
    string? Action = null,
    string? ValidationError = null,
    string? RevisedPrompt = null,
    Guid? PostId = null,
    Guid? ChatId = null,
    Guid? CorrelationId = null,
    string? RetrievalMode = null,
    IReadOnlyList<string>? SourceUrls = null,
    IReadOnlyList<Guid>? ImportedResourceIds = null);
