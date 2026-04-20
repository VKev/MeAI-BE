using SharedLibrary.Common.ResponseModel;

namespace Application.Common;

internal static class FeedErrors
{
    public static readonly Error PostNotFound = new("Feed.Post.NotFound", "The requested post was not found.");
    public static readonly Error PostAlreadyLiked = new("Feed.Post.Like.Exists", "You have already liked this post.");
    public static readonly Error PostNotLiked = new("Feed.Post.Like.NotFound", "You have not liked this post.");
    public static readonly Error CommentNotFound = new("Feed.Comment.NotFound", "The requested comment was not found.");
    public static readonly Error CommentAlreadyLiked = new("Feed.Comment.Like.Exists", "You have already liked this comment.");
    public static readonly Error CommentNotLiked = new("Feed.Comment.Like.NotFound", "You have not liked this comment.");
    public static readonly Error ReportNotFound = new("Feed.Report.NotFound", "The requested report was not found.");
    public static readonly Error UserNotFound = new("Feed.User.NotFound", "The requested user profile was not found.");
    public static readonly Error EmptyPost = new("Feed.Post.Empty", "A post must include content or at least one resource.");
    public static readonly Error EmptyComment = new("Feed.Comment.Empty", "Comment content is required.");
    public static readonly Error CannotFollowYourself = new("Feed.Follow.Self", "You cannot follow yourself.");
    public static readonly Error AlreadyFollowing = new("Feed.Follow.Exists", "You are already following this user.");
    public static readonly Error NotFollowing = new("Feed.Follow.NotFound", "You are not following this user.");
    public static readonly Error Forbidden = new("Feed.Forbidden", "You are not allowed to perform this action.");
    public static readonly Error InvalidReportTarget = new("Feed.Report.Target", "Report target type must be either Post or Comment.");
    public static readonly Error InvalidReportStatus = new("Feed.Report.Status", "Report status is invalid.");
    public static readonly Error InvalidReportAction = new("Feed.Report.Action", "Report action is invalid.");
    public static readonly Error InvalidUsername = new("Feed.Profile.Username", "Username is required.");

    public static Error MissingResources(int expected, int actual) =>
        new("Feed.Resource.Missing", $"Expected {expected} resource(s) but only resolved {actual}.");

    public static Error InvalidReportReason() =>
        new("Feed.Report.Reason", "Report reason is required.");

    public static Error InvalidReportTransition(string status) =>
        new("Feed.Report.StatusTransition", $"Cannot transition report to status '{status}'.");

    public static Error InvalidReportActionForTarget(string action, string targetType) =>
        new("Feed.Report.ActionTarget", $"Action '{action}' is not supported for target type '{targetType}'.");

    public static Error InvalidReportActionRequiresResolved(string action) =>
        new("Feed.Report.ActionStatus", $"Action '{action}' requires the report to be resolved.");
}

