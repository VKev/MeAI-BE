namespace Application.Agents.Models;

public sealed record AgentChatResponse(
    Guid SessionId,
    AgentMessageResponse UserMessage,
    AgentMessageResponse AssistantMessage);
