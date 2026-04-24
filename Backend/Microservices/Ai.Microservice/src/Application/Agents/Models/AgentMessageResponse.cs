namespace Application.Agents.Models;

public sealed record AgentMessageResponse(
    Guid Id,
    Guid SessionId,
    string Role,
    string? Content,
    string? Status,
    string? ErrorMessage,
    string? Model,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<AgentActionResponse> Actions,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
