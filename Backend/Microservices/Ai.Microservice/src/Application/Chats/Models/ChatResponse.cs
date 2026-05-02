namespace Application.Chats.Models;

public sealed record ChatResponse(
    Guid Id,
    Guid SessionId,
    string? Prompt,
    string? Config,
    string? ReferenceResourceIds,
    string? ResultResourceIds,
    IReadOnlyList<string>? ReferenceResourceUrls,
    IReadOnlyList<string>? ResultResourceUrls,
    string? Status,
    string? ErrorMessage,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<ChatResourceInfo>? ReferenceResources = null,
    IReadOnlyList<ChatResourceInfo>? ResultResources = null);

public sealed record ChatResourceInfo(
    Guid ResourceId,
    string Url,
    string? ResourceType,
    string? ContentType);
