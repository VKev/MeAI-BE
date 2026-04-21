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

        var actorIdentity = await ResolveActorIdentityAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreateFollowed(actorUserId, actorIdentity.Username, actorIdentity.AvatarUrl, targetUserId);
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

        var actorIdentity = await ResolveActorIdentityAsync(authorUserId, cancellationToken);

        foreach (var recipientUserId in recipients)
        {
            var notificationEvent = _factory.CreateNewPost(
                authorUserId,
                actorIdentity.Username,
                actorIdentity.FullName,
                actorIdentity.AvatarUrl,
                recipientUserId,
                postId,
                preview);
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

        var actorIdentity = await ResolveActorIdentityAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreateComment(
            actorUserId,
            actorIdentity.Username,
            actorIdentity.AvatarUrl,
            postOwnerUserId,
            postId,
            commentId,
            preview);
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

        var actorIdentity = await ResolveActorIdentityAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreatePostLiked(
            actorUserId,
            actorIdentity.Username,
            actorIdentity.AvatarUrl,
            postOwnerUserId,
            postId,
            preview);
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

        var actorIdentity = await ResolveActorIdentityAsync(actorUserId, cancellationToken);
        var notificationEvent = _factory.CreateCommentLiked(
            actorUserId,
            actorIdentity.Username,
            actorIdentity.AvatarUrl,
            commentOwnerUserId,
            postId,
            commentId,
            preview);
        await _publishEndpoint.Publish(notificationEvent, cancellationToken);
    }

    private async Task<ActorNotificationIdentity> ResolveActorIdentityAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var fallbackValue = actorUserId.ToString();
        var profileResult = await _userResourceService.GetPublicUserProfilesByIdsAsync([actorUserId], cancellationToken);
        if (profileResult.IsSuccess
            && profileResult.Value.TryGetValue(actorUserId, out var profile))
        {
            var username = string.IsNullOrWhiteSpace(profile.Username) ? fallbackValue : profile.Username;
            var fullName = string.IsNullOrWhiteSpace(profile.FullName) ? username : profile.FullName;
            var avatarUrl = string.IsNullOrWhiteSpace(profile.AvatarUrl) ? null : profile.AvatarUrl;
            return new ActorNotificationIdentity(username, fullName, avatarUrl);
        }

        return new ActorNotificationIdentity(fallbackValue, fallbackValue, null);
    }

    private sealed record ActorNotificationIdentity(string Username, string FullName, string? AvatarUrl);
}

