using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using MassTransit;
using SharedLibrary.Contracts.Notifications;

namespace Infrastructure.Logic.Notifications;

public sealed class FeedNotificationService : IFeedNotificationService
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly FeedNotificationFactory _factory;
    private readonly IUserResourceService _userResourceService;

    public FeedNotificationService(
        IPublishEndpoint publishEndpoint,
        FeedNotificationFactory factory,
        IUserResourceService userResourceService)
    {
        _publishEndpoint = publishEndpoint;
        _factory = factory;
        _userResourceService = userResourceService;
    }

    public async Task NotifyFollowedAsync(Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == targetUserId)
        {
            return;
        }

        var username = await ResolveUsernameAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreateFollowed(actorUserId, username, targetUserId);
        await _publishEndpoint.Publish(notificationEvent, cancellationToken);
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

        var username = await ResolveUsernameAsync(authorUserId, cancellationToken);

        foreach (var recipientUserId in recipients)
        {
            var notificationEvent = _factory.CreateNewPost(authorUserId, username, recipientUserId, postId, preview);
            await _publishEndpoint.Publish(notificationEvent, cancellationToken);
        }
    }

    public async Task NotifyCommentAsync(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview,
        CancellationToken cancellationToken)
    {
        if (actorUserId == postOwnerUserId)
        {
            return;
        }

        var username = await ResolveUsernameAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreateComment(actorUserId, username, postOwnerUserId, postId, commentId, preview);
        await _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    public async Task NotifyPostLikedAsync(
        Guid actorUserId,
        Guid postOwnerUserId,
        Guid postId,
        string preview,
        CancellationToken cancellationToken)
    {
        if (actorUserId == postOwnerUserId)
        {
            return;
        }

        var username = await ResolveUsernameAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreatePostLiked(actorUserId, username, postOwnerUserId, postId, preview);
        await _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    public async Task NotifyCommentLikedAsync(
        Guid actorUserId,
        Guid commentOwnerUserId,
        Guid postId,
        Guid commentId,
        string preview,
        CancellationToken cancellationToken)
    {
        if (actorUserId == commentOwnerUserId)
        {
            return;
        }

        var username = await ResolveUsernameAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreateCommentLiked(actorUserId, username, commentOwnerUserId, postId, commentId, preview);
        await _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    private async Task<string> ResolveUsernameAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var profileResult = await _userResourceService.GetPublicUserProfilesByIdsAsync([actorUserId], cancellationToken);
        if (profileResult.IsSuccess
            && profileResult.Value.TryGetValue(actorUserId, out var profile)
            && !string.IsNullOrWhiteSpace(profile.Username))
        {
            return profile.Username;
        }

        return actorUserId.ToString();
    }
}

