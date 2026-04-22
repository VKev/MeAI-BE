using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Publishing;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public sealed class UnpublishFromTargetConsumer : IConsumer<UnpublishFromTargetRequested>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";
    private const string UnpublishingStatus = "unpublishing";
    private const string DraftStatus = "draft";
    private const string FailedStatus = "failed";
    private const int MaxAttempts = 3;

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookPublishService _facebookPublishService;
    private readonly IInstagramPublishService _instagramPublishService;
    private readonly IThreadsPublishService _threadsPublishService;
    private readonly ILogger<UnpublishFromTargetConsumer> _logger;

    public UnpublishFromTargetConsumer(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserSocialMediaService userSocialMediaService,
        IFacebookPublishService facebookPublishService,
        IInstagramPublishService instagramPublishService,
        IThreadsPublishService threadsPublishService,
        ILogger<UnpublishFromTargetConsumer> logger)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userSocialMediaService = userSocialMediaService;
        _facebookPublishService = facebookPublishService;
        _instagramPublishService = instagramPublishService;
        _threadsPublishService = threadsPublishService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UnpublishFromTargetRequested> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "Unpublishing target. PublicationId: {PublicationId}, Type: {Type}, ExternalId: {ExternalId}",
            message.PublicationId, message.SocialMediaType, message.ExternalContentId);

        var publication = await _postPublicationRepository.GetByIdAsync(message.PublicationId, ct);
        if (publication is null || publication.DeletedAt.HasValue)
        {
            _logger.LogWarning("Publication not found or already deleted. Id: {Id}", message.PublicationId);
            return;
        }

        var smResult = await _userSocialMediaService.GetSocialMediasAsync(
            message.UserId, new[] { message.SocialMediaId }, ct);
        if (smResult.IsFailure || smResult.Value.Count == 0)
        {
            await FailAsync(context, message, publication, "SocialMedia.NotFound", "Social media account not found.", ct);
            await FinalizeIfDoneAsync(context, message, ct);
            return;
        }

        using var metadata = ParseMetadata(smResult.Value[0].MetadataJson);

        Error? lastError = null;
        var success = false;

        var isReel = !string.IsNullOrWhiteSpace(publication.ContentType) &&
                     string.Equals(publication.ContentType, "reels", StringComparison.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var deleteResult = await CallPlatformDeleteAsync(message.SocialMediaType, metadata, message.ExternalContentId, isReel, ct);
                if (deleteResult.IsSuccess)
                {
                    success = true;
                    break;
                }
                lastError = deleteResult.Error;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unpublish attempt {Attempt} threw. PublicationId: {Id}", attempt, publication.Id);
                lastError = new Error("Unpublish.Unexpected", ex.Message);
            }

            if (attempt < MaxAttempts)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        if (success)
        {
            // Soft-delete is enough to hide this row from every read path — don't touch
            // PublishStatus here. "draft" isn't in the check constraint, and since DeletedAt
            // is set the stored status value is no longer meaningful.
            publication.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            publication.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _postPublicationRepository.Update(publication);
            await _postPublicationRepository.SaveChangesAsync(ct);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    message.UserId,
                    NotificationTypes.PostUnpublishTargetCompleted,
                    "Post unpublished",
                    $"Removed from {message.SocialMediaType}.",
                    new
                    {
                        message.CorrelationId,
                        message.PostId,
                        message.SocialMediaId,
                        message.SocialMediaType,
                        message.PublicationId
                    },
                    source: NotificationSourceConstants.Creator),
                ct);
        }
        else
        {
            await FailAsync(context, message, publication,
                lastError?.Code ?? "Unpublish.Unknown",
                lastError?.Description ?? "Unpublish failed.",
                ct);
        }

        await FinalizeIfDoneAsync(context, message, ct);
    }

    private async Task<Result<bool>> CallPlatformDeleteAsync(
        string type, JsonDocument? metadata, string externalId, bool isReel, CancellationToken ct)
    {
        if (string.Equals(type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var pageToken = GetMetadataValue(metadata, "page_access_token");
            var userToken = GetMetadataValue(metadata, "user_access_token")
                            ?? GetMetadataValue(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(pageToken) && string.IsNullOrWhiteSpace(userToken))
            {
                return Result.Failure<bool>(new Error("Facebook.DeleteMissingToken", "Missing Facebook access token."));
            }
            return await _facebookPublishService.DeleteAsync(
                new FacebookDeleteRequest(externalId, pageToken ?? string.Empty, userToken, IsReel: isReel), ct);
        }

        if (string.Equals(type, InstagramType, StringComparison.OrdinalIgnoreCase))
        {
            var token = GetMetadataValue(metadata, "access_token")
                        ?? GetMetadataValue(metadata, "user_access_token");
            if (string.IsNullOrWhiteSpace(token))
            {
                return Result.Failure<bool>(new Error("Instagram.DeleteMissingToken", "Missing access token."));
            }
            return await _instagramPublishService.DeleteAsync(
                new InstagramDeleteRequest(externalId, token), ct);
        }

        if (string.Equals(type, ThreadsType, StringComparison.OrdinalIgnoreCase))
        {
            var token = GetMetadataValue(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(token))
            {
                return Result.Failure<bool>(new Error("Threads.DeleteMissingToken", "Missing access token."));
            }
            return await _threadsPublishService.DeleteAsync(
                new ThreadsDeleteRequest(externalId, token), ct);
        }

        if (string.Equals(type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            // TikTok Content Posting API does not expose a delete endpoint.
            return Result.Failure<bool>(new Error(
                "TikTok.DeleteNotSupported",
                "TikTok does not support deleting posts via API. Please delete the post manually in the TikTok app."));
        }

        return Result.Failure<bool>(new Error("Unpublish.UnsupportedPlatform", $"Unsupported platform: {type}"));
    }

    private async Task FailAsync(
        ConsumeContext<UnpublishFromTargetRequested> context,
        UnpublishFromTargetRequested message,
        Domain.Entities.PostPublication publication,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        publication.PublishStatus = FailedStatus;
        publication.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _postPublicationRepository.Update(publication);
        await _postPublicationRepository.SaveChangesAsync(ct);

        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostUnpublishTargetFailed,
                "Post unpublish failed",
                $"Could not remove post from {message.SocialMediaType}.",
                new
                {
                    message.CorrelationId,
                    message.PostId,
                    message.SocialMediaId,
                    message.SocialMediaType,
                    message.PublicationId,
                    errorCode,
                    errorMessage
                },
                source: NotificationSourceConstants.Creator),
            ct);
    }

    private async Task FinalizeIfDoneAsync(
        ConsumeContext<UnpublishFromTargetRequested> context,
        UnpublishFromTargetRequested message,
        CancellationToken ct)
    {
        var publications = await _postPublicationRepository.GetByPostIdForUpdateAsync(message.PostId, ct);
        var stillInFlight = publications.Any(p =>
            !p.DeletedAt.HasValue &&
            string.Equals(p.PublishStatus, UnpublishingStatus, StringComparison.OrdinalIgnoreCase));

        if (stillInFlight) return;

        var post = await _postRepository.GetByIdForUpdateAsync(message.PostId, ct);
        if (post is null) return;

        // Any publication left that isn't soft-deleted means it failed to unpublish.
        var remainingActive = publications.Any(p => !p.DeletedAt.HasValue);
        var finalStatus = remainingActive ? FailedStatus : DraftStatus;

        post.Status = finalStatus;
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(ct);

        // Include per-destination target list so the FE can render platform icons + avatars,
        // one chip PER PAGE on Facebook multi-page rather than collapsing them under a single
        // socialMediaId. `publications` from GetByPostIdForUpdateAsync skips soft-deleted rows,
        // so we re-fetch including deleted to build the full success/failure picture.
        var allPublications = await _postPublicationRepository.GetAllByPostIdIncludingDeletedAsync(message.PostId, ct);
        var successTargets = allPublications
            .Where(p => p.DeletedAt.HasValue)
            .GroupBy(p => new { p.SocialMediaId, p.DestinationOwnerId })
            .Select(g =>
            {
                var first = g.First();
                return new
                {
                    socialMediaId = first.SocialMediaId,
                    socialMediaType = first.SocialMediaType,
                    destinationOwnerId = first.DestinationOwnerId,
                    status = DraftStatus
                };
            });
        var failedTargets = allPublications
            .Where(p => !p.DeletedAt.HasValue)
            .GroupBy(p => new { p.SocialMediaId, p.DestinationOwnerId })
            .Select(g =>
            {
                var first = g.First();
                return new
                {
                    socialMediaId = first.SocialMediaId,
                    socialMediaType = first.SocialMediaType,
                    destinationOwnerId = first.DestinationOwnerId,
                    status = FailedStatus
                };
            });
        var targets = successTargets.Concat(failedTargets).ToList();

        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostUnpublishBatchCompleted,
                remainingActive ? "Unpublish finished with errors" : "Post returned to draft",
                remainingActive
                    ? "Some targets could not be unpublished — check each account for details."
                    : "The post has been removed from every connected account and is back in draft.",
                new
                {
                    message.CorrelationId,
                    message.PostId,
                    finalStatus,
                    targets
                },
                source: NotificationSourceConstants.Creator),
            ct);
    }

    private static JsonDocument? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try { return JsonDocument.Parse(metadataJson); }
        catch (JsonException) { return null; }
    }

    private static string? GetMetadataValue(JsonDocument? metadata, string key)
    {
        if (metadata is null) return null;
        if (metadata.RootElement.ValueKind == JsonValueKind.Object &&
            metadata.RootElement.TryGetProperty(key, out var element))
        {
            return element.GetString();
        }
        return null;
    }
}
