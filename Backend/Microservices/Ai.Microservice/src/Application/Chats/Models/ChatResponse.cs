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
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
