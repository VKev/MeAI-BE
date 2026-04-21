namespace Application.Abstractions.Notifications;

public interface IFeedNotificationService
{
    Task NotifyFollowedAsync(Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken);

    Task NotifyNewPostAsync(
        Guid authorUserId,
        IReadOnlyCollection<Guid> followerUserIds,
        Guid postId,
        string? preview,
        CancellationToken cancellationToken);

    Task NotifyCommentAsync(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview,
        CancellationToken cancellationToken);

    Task NotifyPostLikedAsync(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        string preview,
        CancellationToken cancellationToken);

    Task NotifyCommentLikedAsync(
        Guid actorUserId,
        Guid commentOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview,
        CancellationToken cancellationToken);
}
