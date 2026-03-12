using SharedLibrary.Common.ResponseModel;

namespace Application.ChatSessions;

public static class ChatSessionErrors
{
    public static readonly Error NotFound = new("ChatSession.NotFound", "Chat session not found");
    public static readonly Error Unauthorized = new("ChatSession.Unauthorized", "You are not authorized to access this chat session");
    public static readonly Error WorkspaceNotFound = new("ChatSession.WorkspaceNotFound", "Workspace not found");
    public static readonly Error WorkspaceIdRequired = new("ChatSession.WorkspaceIdRequired", "WorkspaceId is required when ChatSessionId is not provided");
    public static readonly Error WorkspaceMismatch = new("ChatSession.WorkspaceMismatch", "Chat session does not belong to the provided workspace");
}
