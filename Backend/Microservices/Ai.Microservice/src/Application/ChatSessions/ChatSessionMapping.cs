using Application.ChatSessions.Models;
using Domain.Entities;

namespace Application.ChatSessions;

internal static class ChatSessionMapping
{
    public static ChatSessionResponse ToResponse(ChatSession session)
    {
        return new ChatSessionResponse(
            Id: session.Id,
            UserId: session.UserId,
            SessionName: session.SessionName,
            CreatedAt: session.CreatedAt,
            UpdatedAt: session.UpdatedAt);
    }
}
