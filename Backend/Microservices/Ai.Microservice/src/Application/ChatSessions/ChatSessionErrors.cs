using SharedLibrary.Common.ResponseModel;

namespace Application.ChatSessions;

public static class ChatSessionErrors
{
    public static readonly Error NotFound = new("ChatSession.NotFound", "Chat session not found");
    public static readonly Error Unauthorized = new("ChatSession.Unauthorized", "You are not authorized to access this chat session");
}
