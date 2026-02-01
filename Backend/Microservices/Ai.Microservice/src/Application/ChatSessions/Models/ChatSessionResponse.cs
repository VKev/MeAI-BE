namespace Application.ChatSessions.Models;

public sealed record ChatSessionResponse(
    Guid Id,
    Guid UserId,
    string? SessionName,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
