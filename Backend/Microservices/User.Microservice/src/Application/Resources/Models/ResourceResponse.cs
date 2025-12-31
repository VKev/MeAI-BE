namespace Application.Resources.Models;

public sealed record ResourceResponse(
    Guid Id,
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
