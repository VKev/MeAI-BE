using SharedLibrary.Common.ResponseModel;

namespace Application.Posts;

public static class PostErrors
{
    public static readonly Error NotFound = new("Post.NotFound", "Post not found");
    public static readonly Error Unauthorized = new("Post.Unauthorized", "You are not authorized to access this post");
    public static readonly Error WorkspaceNotFound = new("Post.WorkspaceNotFound", "Workspace not found");
    public static readonly Error WorkspaceIdRequired = new("Post.WorkspaceIdRequired", "WorkspaceId is required to publish this post");
}
