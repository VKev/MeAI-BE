namespace Application.Resources.Models;

public sealed record ResourceResponse(
    Guid Id,
    Guid? WorkspaceId,
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType,
    long? SizeBytes,
    string? OriginKind,
    string? OriginSourceUrl,
    Guid? OriginChatSessionId,
    Guid? OriginChatId,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
