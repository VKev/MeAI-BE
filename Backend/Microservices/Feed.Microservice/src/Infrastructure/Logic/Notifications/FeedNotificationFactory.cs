using SharedLibrary.Contracts.Notifications;

namespace Infrastructure.Logic.Notifications;

public sealed class FeedNotificationFactory
{
    public NotificationRequestedEvent CreateFollowed(Guid actorUserId, Guid targetUserId)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            targetUserId,
            "Feed.Followed",
            "You have a new follower",
            "Another MeAI creator started following you.",
            new
            {
                actorUserId
            },
            actorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }

    public NotificationRequestedEvent CreateNewPost(
        Guid authorUserId,
        Guid recipientUserId,
        Guid postId,
        string? preview)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            recipientUserId,
            "Feed.NewPost",
            "A followed creator posted",
            "A creator you follow just published a new post.",
            new
            {
                authorUserId,
                postId,
                preview
            },
            authorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }

    public NotificationRequestedEvent CreateComment(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview)
    {
        return NotificationRequestedEventFactory.CreateForUser(
            postOwnerUserId,
            "Feed.Commented",
            "New comment on your post",
            "Someone commented on your MeAI feed post.",
            new
            {
                actorUserId,
                postId,
                commentId,
                preview
            },
            actorUserId,
            DateTime.UtcNow,
            NotificationSourceConstants.Social);
    }
}
