namespace Application.Chats.Models;

public sealed record WorkspaceAiResourceResponse(
    Guid ChatSessionId,
    Guid ChatId,
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    DateTime? ChatCreatedAt);
