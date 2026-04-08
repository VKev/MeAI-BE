using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts;

internal static class PostBuilderErrors
{
    public static readonly Error NotFound =
        new("PostBuilder.NotFound", "Post builder not found");

    public static readonly Error Unauthorized =
        new("PostBuilder.Unauthorized", "You are not allowed to access this post builder");
}
