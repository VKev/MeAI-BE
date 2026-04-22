using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.SocialMedias;
using Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Publishing;

namespace Infrastructure.Logic.Consumers;

public sealed class UpdatePublishedTargetConsumer : IConsumer<UpdatePublishedTargetRequested>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string ThreadsType = "threads";
    private const string TikTokType = "tiktok";

    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookPublishService _facebookPublishService;
    private readonly ILogger<UpdatePublishedTargetConsumer> _logger;

    public UpdatePublishedTargetConsumer(
        IPostPublicationRepository postPublicationRepository,
        IUserSocialMediaService userSocialMediaService,
        IFacebookPublishService facebookPublishService,
        ILogger<UpdatePublishedTargetConsumer> logger)
    {
        _postPublicationRepository = postPublicationRepository;
        _userSocialMediaService = userSocialMediaService;
        _facebookPublishService = facebookPublishService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UpdatePublishedTargetRequested> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation(
            "Updating published target. PublicationId: {Id}, Type: {Type}",
            message.PublicationId, message.SocialMediaType);

        var publication = await _postPublicationRepository.GetByIdAsync(message.PublicationId, ct);
        if (publication is null || publication.DeletedAt.HasValue)
        {
            await PublishFailureAsync(context, message, "Publication.NotFound", "Publication no longer exists.");
            return;
        }

        var smResult = await _userSocialMediaService.GetSocialMediasAsync(
            message.UserId, new[] { message.SocialMediaId }, ct);
        if (smResult.IsFailure || smResult.Value.Count == 0)
        {
            await PublishFailureAsync(context, message, "SocialMedia.NotFound", "Social media account not found.");
            return;
        }

        using var metadata = ParseMetadata(smResult.Value[0].MetadataJson);

        Result<bool> result;

        if (string.Equals(message.SocialMediaType, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var pageToken = GetMetadataValue(metadata, "page_access_token");
            var userToken = GetMetadataValue(metadata, "user_access_token")
                            ?? GetMetadataValue(metadata, "access_token");
            if (string.IsNullOrWhiteSpace(pageToken) && string.IsNullOrWhiteSpace(userToken))
            {
                await PublishFailureAsync(context, message, "Facebook.UpdateMissingToken", "Missing Facebook access token.");
                return;
            }
            result = await _facebookPublishService.UpdateAsync(
                new FacebookUpdateRequest(message.ExternalContentId, pageToken ?? string.Empty, message.NewCaption, userToken), ct);
        }
        else if (string.Equals(message.SocialMediaType, InstagramType, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.SocialMediaType, ThreadsType, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.SocialMediaType, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            await PublishFailureAsync(
                context, message,
                $"{message.SocialMediaType}.UpdateNotSupported",
                $"{message.SocialMediaType} does not support editing a post after publishing. Unpublish and repost to change the caption.");
            return;
        }
        else
        {
            await PublishFailureAsync(context, message,
                "Update.UnsupportedPlatform", $"Unsupported platform: {message.SocialMediaType}");
            return;
        }

        if (result.IsSuccess)
        {
            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    message.UserId,
                    NotificationTypes.PostUpdateTargetCompleted,
                    "Post caption updated",
                    $"Updated caption on {message.SocialMediaType}.",
                    new { message.CorrelationId, message.PostId, message.SocialMediaId, message.SocialMediaType, message.PublicationId },
                    source: NotificationSourceConstants.Creator),
                ct);
        }
        else
        {
            await PublishFailureAsync(context, message, result.Error.Code, result.Error.Description);
        }

        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostUpdateBatchCompleted,
                "Post update finished",
                "Caption update has been processed.",
                new { message.CorrelationId, message.PostId },
                source: NotificationSourceConstants.Creator),
            ct);
    }

    private static async Task PublishFailureAsync(
        ConsumeContext<UpdatePublishedTargetRequested> context,
        UpdatePublishedTargetRequested message,
        string errorCode,
        string errorMessage)
    {
        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostUpdateTargetFailed,
                "Post caption update failed",
                $"Could not update caption on {message.SocialMediaType}.",
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
                source: NotificationSourceConstants.Creator));
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
