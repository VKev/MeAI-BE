namespace Application.Resources.Models;

public sealed record ResourceResponse(
    Guid Id,
    Guid? WorkspaceId,
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
