namespace Application.Agents.Models;

public sealed record AgentChatMetadata(
    string Role,
    string? Model = null,
    IReadOnlyList<string>? ToolNames = null,
    IReadOnlyList<AgentActionResponse>? Actions = null,
    string? RetrievalMode = null,
    IReadOnlyList<string>? SourceUrls = null,
    IReadOnlyList<Guid>? ImportedResourceIds = null);
