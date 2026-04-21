using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Publishing;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public sealed class PublishToTargetConsumer : IConsumer<PublishToTargetRequested>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";
    private const string PostsType = "posts";
    private const string ProcessingStatus = "processing";
    private const string PublishedStatus = "published";
    private const string FailedStatus = "failed";
    private const int MaxAttempts = 3;

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookPublishService _facebookPublishService;
    private readonly IInstagramPublishService _instagramPublishService;
    private readonly ITikTokPublishService _tikTokPublishService;
    private readonly IThreadsPublishService _threadsPublishService;
    private readonly ILogger<PublishToTargetConsumer> _logger;

    public PublishToTargetConsumer(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserResourceService userResourceService,
        IUserSocialMediaService userSocialMediaService,
        IFacebookPublishService facebookPublishService,
        IInstagramPublishService instagramPublishService,
        ITikTokPublishService tikTokPublishService,
        IThreadsPublishService threadsPublishService,
        ILogger<PublishToTargetConsumer> logger)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userResourceService = userResourceService;
        _userSocialMediaService = userSocialMediaService;
        _facebookPublishService = facebookPublishService;
        _instagramPublishService = instagramPublishService;
        _tikTokPublishService = tikTokPublishService;
        _threadsPublishService = threadsPublishService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PublishToTargetRequested> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Publishing target started. CorrelationId: {CorrelationId}, PostId: {PostId}, SocialMediaId: {SocialMediaId}, Type: {Type}",
            message.CorrelationId,
            message.PostId,
            message.SocialMediaId,
            message.SocialMediaType);

        var placeholder = await _postPublicationRepository.GetByIdAsync(message.PublicationId, cancellationToken);
        if (placeholder is null)
        {
            _logger.LogWarning(
                "Placeholder publication not found. PublicationId: {PublicationId}", message.PublicationId);
            return;
        }

        if (!string.Equals(placeholder.PublishStatus, ProcessingStatus, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Placeholder already finalized; skipping. PublicationId: {PublicationId}, Status: {Status}",
                message.PublicationId,
                placeholder.PublishStatus);
            return;
        }

        var post = await _postRepository.GetByIdForUpdateAsync(message.PostId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            await MarkPlaceholderFailedAsync(placeholder, "Post.NotFound", "Post was deleted before publish.", cancellationToken);
            await FirePerTargetFailureAsync(context, message, "Post.NotFound", "Post was deleted before publish.");
            await FinalizePostStatusIfDoneAsync(context, message, post, cancellationToken);
            return;
        }

        var socialMediasResult = await _userSocialMediaService.GetSocialMediasAsync(
            message.UserId, new[] { message.SocialMediaId }, cancellationToken);

        if (socialMediasResult.IsFailure || socialMediasResult.Value.Count == 0)
        {
            var error = socialMediasResult.IsFailure
                ? socialMediasResult.Error
                : new Error("SocialMedia.NotFound", "Social media account not found.");

            await MarkPlaceholderFailedAsync(placeholder, error.Code, error.Description, cancellationToken);
            await FirePerTargetFailureAsync(context, message, error.Code, error.Description);
            await FinalizePostStatusIfDoneAsync(context, message, post, cancellationToken);
            return;
        }

        var socialMedia = socialMediasResult.Value[0];

        var resourceIds = ExtractResourceIds(post.Content);
        IReadOnlyList<UserResourcePresignResult> presignedResources = Array.Empty<UserResourcePresignResult>();

        var requiresResources = !string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase);
        if (requiresResources || resourceIds.Count > 0)
        {
            if (resourceIds.Count == 0)
            {
                await MarkPlaceholderFailedAsync(placeholder, "Post.MissingResources", "This post has no resources to publish.", cancellationToken);
                await FirePerTargetFailureAsync(context, message, "Post.MissingResources", "This post has no resources to publish.");
                await FinalizePostStatusIfDoneAsync(context, message, post, cancellationToken);
                return;
            }

            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                message.UserId, resourceIds, cancellationToken);

            if (presignResult.IsFailure)
            {
                await MarkPlaceholderFailedAsync(placeholder, presignResult.Error.Code, presignResult.Error.Description, cancellationToken);
                await FirePerTargetFailureAsync(context, message, presignResult.Error.Code, presignResult.Error.Description);
                await FinalizePostStatusIfDoneAsync(context, message, post, cancellationToken);
                return;
            }

            presignedResources = presignResult.Value;
        }

        Error? lastError = null;
        IReadOnlyList<(string PageId, string ExternalId)>? publishedDestinations = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var publishResult = await PublishToSocialMediaAsync(
                    post,
                    socialMedia,
                    presignedResources,
                    message.IsPrivate,
                    cancellationToken);

                if (publishResult.IsSuccess)
                {
                    publishedDestinations = publishResult.Value;
                    break;
                }

                lastError = publishResult.Error;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Publish attempt {Attempt}/{Max} threw. CorrelationId: {CorrelationId}, PublicationId: {PublicationId}",
                    attempt,
                    MaxAttempts,
                    message.CorrelationId,
                    message.PublicationId);
                lastError = new Error("Publish.Unexpected", ex.Message);
            }

            if (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(attempt * 3);
                _logger.LogInformation(
                    "Retrying publish in {Delay}s. Attempt: {Attempt}/{Max}, PublicationId: {PublicationId}",
                    delay.TotalSeconds,
                    attempt,
                    MaxAttempts,
                    message.PublicationId);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        if (publishedDestinations is not null)
        {
            await HandleSuccessAsync(context, message, placeholder, post, socialMedia, publishedDestinations, cancellationToken);
        }
        else
        {
            var errorCode = lastError?.Code ?? "Publish.Unknown";
            var errorMessage = lastError?.Description ?? "Publish failed for unknown reason.";
            await MarkPlaceholderFailedAsync(placeholder, errorCode, errorMessage, cancellationToken);
            await FirePerTargetFailureAsync(context, message, errorCode, errorMessage);
        }

        await FinalizePostStatusIfDoneAsync(context, message, post, cancellationToken);
    }

    private async Task HandleSuccessAsync(
        ConsumeContext<PublishToTargetRequested> context,
        PublishToTargetRequested message,
        PostPublication placeholder,
        Post post,
        UserSocialMediaResult socialMedia,
        IReadOnlyList<(string PageId, string ExternalId)> destinations,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var contentType = post.Content?.PostType ?? PostsType;
        var idType = string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase)
            ? "publish_id"
            : "post_id";

        placeholder.DeletedAt = now;
        _postPublicationRepository.Update(placeholder);

        var newRows = destinations.Select(destination => new PostPublication
        {
            Id = Guid.CreateVersion7(),
            PostId = post.Id,
            WorkspaceId = post.WorkspaceId!.Value,
            SocialMediaId = socialMedia.SocialMediaId,
            SocialMediaType = socialMedia.Type,
            DestinationOwnerId = destination.PageId,
            ExternalContentId = destination.ExternalId,
            ExternalContentIdType = idType,
            ContentType = contentType,
            PublishStatus = PublishedStatus,
            PublishedAt = now,
            CreatedAt = now
        }).ToList();

        await _postPublicationRepository.AddRangeAsync(newRows, cancellationToken);
        await _postPublicationRepository.SaveChangesAsync(cancellationToken);

        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostPublishTargetCompleted,
                "Post published",
                $"Published to {socialMedia.Type}.",
                new
                {
                    message.CorrelationId,
                    message.PostId,
                    message.SocialMediaId,
                    message.SocialMediaType,
                    destinations = destinations.Select(d => new
                    {
                        pageId = d.PageId,
                        externalContentId = d.ExternalId
                    }).ToList()
                },
                createdAt: now,
                source: NotificationSourceConstants.Creator),
            cancellationToken);

        _logger.LogInformation(
            "Publish target succeeded. PublicationId: {PublicationId}, Destinations: {Count}",
            message.PublicationId,
            destinations.Count);
    }

    private async Task MarkPlaceholderFailedAsync(
        PostPublication placeholder,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        placeholder.PublishStatus = FailedStatus;
        placeholder.UpdatedAt = now;
        _postPublicationRepository.Update(placeholder);
        await _postPublicationRepository.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Placeholder marked failed. PublicationId: {PublicationId}, Code: {Code}, Message: {Message}",
            placeholder.Id,
            errorCode,
            errorMessage);
    }

    private static async Task FirePerTargetFailureAsync(
        ConsumeContext<PublishToTargetRequested> context,
        PublishToTargetRequested message,
        string errorCode,
        string errorMessage)
    {
        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostPublishTargetFailed,
                "Post publish failed",
                $"Could not publish to {message.SocialMediaType}.",
                new
                {
                    message.CorrelationId,
                    message.PostId,
                    message.SocialMediaId,
                    message.SocialMediaType,
                    errorCode,
                    errorMessage
                },
                source: NotificationSourceConstants.Creator));
    }

    private async Task FinalizePostStatusIfDoneAsync(
        ConsumeContext<PublishToTargetRequested> context,
        PublishToTargetRequested message,
        Post? post,
        CancellationToken cancellationToken)
    {
        if (post is null) return;

        var publications = await _postPublicationRepository.GetByPostIdForUpdateAsync(post.Id, cancellationToken);
        var stillProcessing = publications.Any(p =>
            string.Equals(p.PublishStatus, ProcessingStatus, StringComparison.OrdinalIgnoreCase) &&
            !p.DeletedAt.HasValue);

        if (stillProcessing)
        {
            return;
        }

        var anyPublished = publications.Any(p =>
            string.Equals(p.PublishStatus, PublishedStatus, StringComparison.OrdinalIgnoreCase) &&
            !p.DeletedAt.HasValue);

        var finalStatus = anyPublished ? PublishedStatus : FailedStatus;
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        post.Status = finalStatus;
        post.UpdatedAt = now;
        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        await context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.PostPublishBatchCompleted,
                anyPublished ? "Post publishing finished" : "Post publishing failed",
                anyPublished
                    ? "Your post finished publishing — check the builder for per-target details."
                    : "All publish targets failed. Your post remains in draft state.",
                new
                {
                    message.CorrelationId,
                    message.PostId,
                    finalStatus
                },
                createdAt: now,
                source: NotificationSourceConstants.Creator),
            cancellationToken);

        _logger.LogInformation(
            "Post batch finalized. PostId: {PostId}, Status: {Status}",
            post.Id,
            finalStatus);
    }

    private async Task<Result<IReadOnlyList<(string PageId, string ExternalId)>>> PublishToSocialMediaAsync(
        Post post,
        UserSocialMediaResult socialMedia,
        IReadOnlyList<UserResourcePresignResult> presignedResources,
        bool? isPrivate,
        CancellationToken cancellationToken)
    {
        var caption = post.Content?.Content?.Trim() ?? string.Empty;
        using var metadata = ParseMetadata(socialMedia.MetadataJson);

        if (string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            if (presignedResources.Count == 0)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("TikTok.MissingMedia", "TikTok publishing requires at least one video."));
            }

            if (presignedResources.Count > 1)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("TikTok.UnsupportedMedia", "TikTok publishing currently supports only one video."));
            }

            var accessToken = GetMetadataValue(metadata, "access_token");
            var openId = GetMetadataValue(metadata, "open_id");

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("TikTok.InvalidToken", "Access token not found in social media metadata."));
            }

            if (string.IsNullOrWhiteSpace(openId))
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("TikTok.InvalidAccount", "TikTok open_id is missing in social media metadata."));
            }

            var resource = presignedResources[0];
            var publishResult = await _tikTokPublishService.PublishAsync(
                new TikTokPublishRequest(
                    AccessToken: accessToken,
                    OpenId: openId,
                    Caption: caption,
                    Media: new TikTokPublishMedia(
                        resource.PresignedUrl,
                        resource.ContentType ?? resource.ResourceType),
                    IsPrivate: isPrivate),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<(string, string)>>(
                new[] { (publishResult.Value.OpenId, publishResult.Value.PublishId) });
        }

        if (string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var userAccessToken = GetMetadataValue(metadata, "user_access_token")
                                  ?? GetMetadataValue(metadata, "access_token");

            if (string.IsNullOrWhiteSpace(userAccessToken))
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
            }

            var publishResult = await _facebookPublishService.PublishAsync(
                new FacebookPublishRequest(
                    UserAccessToken: userAccessToken,
                    PageId: GetMetadataValue(metadata, "page_id"),
                    PageAccessToken: GetMetadataValue(metadata, "page_access_token"),
                    Message: caption,
                    Media: presignedResources
                        .Select(resource => new FacebookPublishMedia(
                            resource.PresignedUrl,
                            resource.ContentType ?? resource.ResourceType))
                        .ToList()),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<(string, string)>>(
                publishResult.Value
                    .Select(result => (result.PageId, result.PostId))
                    .ToList());
        }

        if (string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase))
        {
            if (presignedResources.Count != 1)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("Instagram.UnsupportedMedia", "Instagram publishing currently supports only one media item."));
            }

            var instagramUserId = GetMetadataValue(metadata, "instagram_business_account_id")
                                  ?? GetMetadataValue(metadata, "user_id");

            if (string.IsNullOrWhiteSpace(instagramUserId))
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("Instagram.InvalidAccount", "Instagram business account id is missing in social media metadata."));
            }

            var instagramAccessToken = GetMetadataValue(metadata, "access_token")
                                       ?? GetMetadataValue(metadata, "user_access_token");

            if (string.IsNullOrWhiteSpace(instagramAccessToken))
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("Instagram.InvalidToken", "Access token not found in social media metadata."));
            }

            var resource = presignedResources[0];
            var publishResult = await _instagramPublishService.PublishAsync(
                new InstagramPublishRequest(
                    AccessToken: instagramAccessToken,
                    InstagramUserId: instagramUserId,
                    Caption: caption,
                    Media: new InstagramPublishMedia(
                        resource.PresignedUrl,
                        resource.ContentType ?? resource.ResourceType)),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<(string, string)>>(
                new[] { (publishResult.Value.InstagramUserId, publishResult.Value.PostId) });
        }

        // Threads
        if (presignedResources.Count > 1)
        {
            return Result.Failure<IReadOnlyList<(string, string)>>(
                new Error("Threads.UnsupportedMedia", "Threads publishing currently supports one media item at a time."));
        }

        var threadsUserId = GetMetadataValue(metadata, "user_id");
        if (string.IsNullOrWhiteSpace(threadsUserId))
        {
            return Result.Failure<IReadOnlyList<(string, string)>>(
                new Error("Threads.InvalidAccount", "Threads user id is missing in social media metadata."));
        }

        var threadsAccessToken = GetMetadataValue(metadata, "access_token");
        if (string.IsNullOrWhiteSpace(threadsAccessToken))
        {
            return Result.Failure<IReadOnlyList<(string, string)>>(
                new Error("Threads.InvalidToken", "Access token not found in social media metadata."));
        }

        ThreadsPublishMedia? media = null;
        if (presignedResources.Count == 1)
        {
            var resource = presignedResources[0];
            media = new ThreadsPublishMedia(
                resource.PresignedUrl,
                resource.ContentType ?? resource.ResourceType);
        }

        var threadsResult = await _threadsPublishService.PublishAsync(
            new ThreadsPublishRequest(
                AccessToken: threadsAccessToken,
                ThreadsUserId: threadsUserId,
                Text: caption,
                Media: media),
            cancellationToken);

        if (threadsResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<(string, string)>>(threadsResult.Error);
        }

        return Result.Success<IReadOnlyList<(string, string)>>(
            new[] { (threadsResult.Value.ThreadsUserId, threadsResult.Value.PostId) });
    }

    private static IReadOnlyList<Guid> ExtractResourceIds(PostContent? content)
    {
        if (content?.ResourceList == null || content.ResourceList.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var ids = new List<Guid>();
        foreach (var value in content.ResourceList)
        {
            if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
            {
                ids.Add(parsed);
            }
        }

        return ids;
    }

    private static JsonDocument? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(metadataJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetMetadataValue(JsonDocument? metadata, string key)
    {
        if (metadata == null)
        {
            return null;
        }

        if (metadata.RootElement.ValueKind == JsonValueKind.Object &&
            metadata.RootElement.TryGetProperty(key, out var element))
        {
            return element.GetString();
        }

        return null;
    }
}
