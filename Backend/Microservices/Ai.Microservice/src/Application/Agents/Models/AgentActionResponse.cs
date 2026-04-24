namespace Application.Agents.Models;

public sealed record AgentActionResponse(
    string Type,
    string ToolName,
    string Status,
    string? EntityType = null,
    Guid? EntityId = null,
    string? Label = null,
    string? Summary = null,
    DateTime? OccurredAt = null);
