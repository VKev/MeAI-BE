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
        CancellationToken cancellationToken)
    {
        var requestedAt = DateTimeExtensions.PostgreSqlUtcNow;

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
                        Platform = socialMedia.Type,
                        RequestedAt = requestedAt
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
