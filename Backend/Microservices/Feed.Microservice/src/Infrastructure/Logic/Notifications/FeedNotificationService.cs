using Application.Abstractions.Notifications;
using MassTransit;
using SharedLibrary.Contracts.Notifications;

namespace Infrastructure.Logic.Notifications;

public sealed class FeedNotificationService : IFeedNotificationService
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly FeedNotificationFactory _factory;

    public FeedNotificationService(IPublishEndpoint publishEndpoint, FeedNotificationFactory factory)
    {
        _publishEndpoint = publishEndpoint;
        _factory = factory;
    }

    public Task NotifyFollowedAsync(Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == targetUserId)
        {
            return Task.CompletedTask;
        }

        var notificationEvent = _factory.CreateFollowed(actorUserId, targetUserId);
        return _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    public async Task NotifyNewPostAsync(
        Guid authorUserId,
        IReadOnlyCollection<Guid> followerUserIds,
        Guid postId,
        string? preview,
        CancellationToken cancellationToken)
    {
        var recipients = followerUserIds
            .Where(id => id != Guid.Empty && id != authorUserId)
            .Distinct()
            .ToList();

        foreach (var recipientUserId in recipients)
        {
            var notificationEvent = _factory.CreateNewPost(authorUserId, recipientUserId, postId, preview);
            await _publishEndpoint.Publish(notificationEvent, cancellationToken);
        }
    }

    public Task NotifyCommentAsync(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview,
        CancellationToken cancellationToken)
    {
        if (actorUserId == postOwnerUserId)
        {
            return Task.CompletedTask;
        }

        var notificationEvent = _factory.CreateComment(actorUserId, postOwnerUserId, postId, commentId, preview);
        return _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    public Task NotifyPostLikedAsync(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        string preview,
        CancellationToken cancellationToken)
    {
        if (actorUserId == postOwnerUserId)
        {
            return Task.CompletedTask;
        }

        var notificationEvent = _factory.CreatePostLiked(actorUserId, postOwnerUserId, postId, preview);
        return _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    public Task NotifyCommentLikedAsync(
        Guid actorUserId,
        Guid commentOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview,
        CancellationToken cancellationToken)
    {
        if (actorUserId == commentOwnerUserId)
        {
            return Task.CompletedTask;
        }

        var notificationEvent = _factory.CreateCommentLiked(actorUserId, commentOwnerUserId, postId, commentId, preview);
        return _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }
}
