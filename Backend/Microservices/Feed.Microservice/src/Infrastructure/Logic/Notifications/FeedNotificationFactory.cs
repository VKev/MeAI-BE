using SharedLibrary.Contracts.Notifications;

namespace Infrastructure.Logic.Notifications;

public sealed class FeedNotificationFactory
{
    public NotificationRequestedEvent CreateFollowed(
        Guid actorUserId,
        string username,
        string? avatarUrl,
        Guid targetUserId)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            targetUserId,
            "Feed.Followed",
            "You have a new follower",
            $"{username} started following you.",
            new
            {
                actorUserId,
                username,
                avatarUrl
            },
            actorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }

    public NotificationRequestedEvent CreateNewPost(
        Guid authorUserId,
        string username,
        string fullName,
        string? avatarUrl,
        Guid recipientUserId,
        Guid postId,
        string? preview)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            recipientUserId,
            "Feed.NewPost",
            $"{fullName} has just posted",
            $"{username} shared a new post.",
            new
            {
                authorUserId,
                username,
                avatarUrl,
                postId,
                preview
            },
            authorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }

    public NotificationRequestedEvent CreateComment(
        Guid actorUserId,
        string username,
        string? avatarUrl,
        Guid postOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            postOwnerUserId,
            "Feed.Commented",
            "New comment on your post",
            $"{username} left a comment on your post.",
            new
            {
                actorUserId,
                username,
                avatarUrl,
                postId,
                commentId,
                preview
            },
            actorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }

    public NotificationRequestedEvent CreatePostLiked(
        Guid actorUserId,
        string username,
        string? avatarUrl,
        Guid postOwnerUserId,
        Guid postId,
        string preview)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            postOwnerUserId,
            "Feed.PostLiked",
            "New interaction on your post",
            $"{username} interacted with your post.",
            new
            {
                actorUserId,
                username,
                avatarUrl,
                postId,
                preview
            },
            actorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }

    public NotificationRequestedEvent CreateCommentLiked(
        Guid actorUserId,
        string username,
        string? avatarUrl,
        Guid commentOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            commentOwnerUserId,
            "Feed.CommentLiked",
            "New interaction on your comment",
            $"{username} interacted with your comment.",
            new
            {
                actorUserId,
                username,
                avatarUrl,
                postId,
                commentId,
                preview
            },
            actorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }
}
