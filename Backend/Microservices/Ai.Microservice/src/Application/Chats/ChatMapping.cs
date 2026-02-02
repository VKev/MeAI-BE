using Application.Chats.Models;
using Domain.Entities;

namespace Application.Chats;

internal static class ChatMapping
{
    public static ChatResponse ToResponse(
        Chat chat,
        IReadOnlyList<string>? referenceResourceUrls = null,
        IReadOnlyList<string>? resultResourceUrls = null)
    {
        return new ChatResponse(
            chat.Id,
            chat.SessionId,
            chat.Prompt,
            chat.Config,
            chat.ReferenceResourceIds,
            chat.ResultResourceIds,
            referenceResourceUrls,
            resultResourceUrls,
            chat.CreatedAt,
            chat.UpdatedAt);
    }
}
