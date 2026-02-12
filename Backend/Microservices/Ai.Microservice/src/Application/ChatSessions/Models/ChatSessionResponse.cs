namespace Application.ChatSessions.Models;

public sealed record ChatSessionResponse(
    Guid Id,
    Guid UserId,
    Guid WorkspaceId,
    string? SessionName,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
