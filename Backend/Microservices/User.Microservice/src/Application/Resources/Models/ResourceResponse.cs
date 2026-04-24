namespace Application.Resources.Models;

public sealed record ResourceResponse(
    Guid Id,
    Guid? WorkspaceId,
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType,
    long? SizeBytes,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
