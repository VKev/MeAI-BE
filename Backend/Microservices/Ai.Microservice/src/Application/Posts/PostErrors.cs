using SharedLibrary.Common.ResponseModel;

namespace Application.Posts;

public static class PostErrors
{
    public static readonly Error NotFound = new("Post.NotFound", "Post not found");
    public static readonly Error Unauthorized = new("Post.Unauthorized", "You are not authorized to access this post");
}
