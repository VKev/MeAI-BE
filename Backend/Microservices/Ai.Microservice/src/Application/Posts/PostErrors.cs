using SharedLibrary.Common.ResponseModel;

namespace Application.Posts;

public static class PostErrors
{
    public static readonly Error NotFound = new("Post.NotFound", "Post not found");
    public static readonly Error Unauthorized = new("Post.Unauthorized", "You are not authorized to access this post");
    public static readonly Error WorkspaceNotFound = new("Post.WorkspaceNotFound", "Workspace not found");
    public static readonly Error WorkspaceIdRequired = new("Post.WorkspaceIdRequired", "WorkspaceId is required to publish this post");
    public static readonly Error ScheduleInPast = new("Post.ScheduleInPast", "ScheduledAtUtc must be in the future");
    public static readonly Error ScheduleMissingTargets = new("Post.ScheduleMissingTargets", "At least one social media id is required to schedule this post");
    public static readonly Error ScheduleRequiresWorkspace = new("Post.ScheduleRequiresWorkspace", "WorkspaceId is required to schedule this post");
    public static readonly Error ScheduleAlreadyPublished = new("Post.ScheduleAlreadyPublished", "Published or in-progress posts cannot be scheduled");
}
