using SharedLibrary.Common.ResponseModel;

namespace Application.Chats;

public static class ChatErrors
{
    public static readonly Error NotFound = new("Chat.NotFound", "Chat not found");
    public static readonly Error Unauthorized = new("Chat.Unauthorized", "You are not authorized to access this chat");
}
