using Domain.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.SocialMedia;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

internal static class SocialMediaPostSyncEventPublisher
{
    public static async Task PublishAsync(
        IPublishEndpoint publishEndpoint,
        ILogger logger,
        Guid userId,
        IEnumerable<SocialMedia> socialMedias,
        CancellationToken cancellationToken,
        Guid? workspaceId = null,
        string trigger = "oauth_callback",
        bool removeFromWorkspace = false,
        DateTime? requestedAt = null)
    {
        var batchRequestedAt = requestedAt ?? DateTimeExtensions.PostgreSqlUtcNow;

        foreach (var socialMedia in socialMedias)
        {
            try
            {
                await publishEndpoint.Publish(
                    new SyncSocialMediaPostsRequested
                    {
                        CorrelationId = Guid.CreateVersion7(),
                        UserId = userId,
                        SocialMediaId = socialMedia.Id,
                        WorkspaceId = workspaceId,
                        Platform = socialMedia.Type,
                        ExternalAccountKey = SocialMediaExternalAccountKey.Resolve(socialMedia),
                        Trigger = trigger,
                        RemoveFromWorkspace = removeFromWorkspace,
                        RequestedAt = batchRequestedAt
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to queue social media post sync after OAuth callback. UserId: {UserId}, SocialMediaId: {SocialMediaId}, Platform: {Platform}",
                    userId,
                    socialMedia.Id,
                    socialMedia.Type);
            }
        }
    }
}
