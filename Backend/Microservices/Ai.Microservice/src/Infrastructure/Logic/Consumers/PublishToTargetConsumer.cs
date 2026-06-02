using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.PublishingSchedules;
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
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
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
        IPublishingScheduleRepository publishingScheduleRepository,
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
        _publishingScheduleRepository = publishingScheduleRepository;
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
            await FinalizePostStatusIfDoneAsync(message, post, cancellationToken);
            await FirePerTargetFailureAsync(context, message, "Post.NotFound", "Post was deleted before publish.");
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
            await FinalizePostStatusIfDoneAsync(message, post, cancellationToken);
            await FirePerTargetFailureAsync(context, message, error.Code, error.Description);
            return;
        }

        var socialMedia = socialMediasResult.Value[0];

        var resourceIds = ExtractResourceIds(post.Content);
        IReadOnlyList<UserResourcePresignResult> presignedResources = Array.Empty<UserResourcePresignResult>();

        var requiresResources = RequiresResources(socialMedia.Type, post.Content?.PostType);
        if (requiresResources || resourceIds.Count > 0)
        {
            if (resourceIds.Count == 0)
            {
                await MarkPlaceholderFailedAsync(placeholder, "Post.MissingResources", "This post has no resources to publish.", cancellationToken);
                await FinalizePostStatusIfDoneAsync(message, post, cancellationToken);
                await FirePerTargetFailureAsync(context, message, "Post.MissingResources", "This post has no resources to publish.");
                return;
            }

            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                message.UserId, resourceIds, cancellationToken);

            if (presignResult.IsFailure)
            {
                await MarkPlaceholderFailedAsync(placeholder, presignResult.Error.Code, presignResult.Error.Description, cancellationToken);
                await FinalizePostStatusIfDoneAsync(message, post, cancellationToken);
                await FirePerTargetFailureAsync(context, message, presignResult.Error.Code, presignResult.Error.Description);
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
                _logger.LogWarning(
                    "Publish attempt {Attempt}/{Max} failed. CorrelationId: {CorrelationId}, PublicationId: {PublicationId}, PostId: {PostId}, SocialMediaId: {SocialMediaId}, Platform: {Platform}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
                    attempt,
                    MaxAttempts,
                    message.CorrelationId,
                    message.PublicationId,
                    message.PostId,
                    message.SocialMediaId,
                    socialMedia.Type,
                    lastError.Code,
                    lastError.Description);
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
            await HandleSuccessAsync(placeholder, post, socialMedia, publishedDestinations, cancellationToken);
            await FinalizePostStatusIfDoneAsync(message, post, cancellationToken);
            await FirePerDestinationSuccessAsync(context, message, socialMedia.Type, publishedDestinations, cancellationToken);
        }
        else
        {
            var errorCode = lastError?.Code ?? "Publish.Unknown";
            var errorMessage = lastError?.Description ?? "Publish failed for unknown reason.";
            await MarkPlaceholderFailedAsync(placeholder, errorCode, errorMessage, cancellationToken);
            await FinalizePostStatusIfDoneAsync(message, post, cancellationToken);
            await FirePerTargetFailureAsync(context, message, errorCode, errorMessage);
        }
    }

    private async Task HandleSuccessAsync(
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
            WorkspaceId = placeholder.WorkspaceId,
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

        _logger.LogInformation(
            "Publish target succeeded. PublicationId: {PublicationId}, Destinations: {Count}",
            placeholder.Id,
            destinations.Count);
    }

    private static async Task FirePerDestinationSuccessAsync(
        ConsumeContext<PublishToTargetRequested> context,
        PublishToTargetRequested message,
        string socialMediaType,
        IReadOnlyList<(string PageId, string ExternalId)> destinations,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        foreach (var destination in destinations)
        {
            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    message.UserId,
                    NotificationTypes.PostPublishTargetCompleted,
                    "Post published",
                    $"Published to {socialMediaType}.",
                    new
                    {
                        message.CorrelationId,
                        message.PostId,
                        message.SocialMediaId,
                        message.SocialMediaType,
                        destinations = new[]
                        {
                            new
                            {
                                pageId = destination.PageId,
                                externalContentId = destination.ExternalId
                            }
                        }
                    },
                    createdAt: now,
                    source: NotificationSourceConstants.Creator),
                cancellationToken);
        }
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

        if (message.PublishingScheduleId.HasValue)
        {
            await UpdatePublishingScheduleAsync(
                message.PublishingScheduleId.Value,
                post.Id,
                finalStatus,
                cancellationToken);
        }

        _logger.LogInformation(
            "Post finalized after all publish targets completed. PostId: {PostId}, Status: {Status}",
            post.Id,
            finalStatus);
    }

    private async Task UpdatePublishingScheduleAsync(
        Guid scheduleId,
        Guid postId,
        string finalStatus,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(scheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return;
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        schedule.LastExecutionAt = now;
        schedule.UpdatedAt = now;
        schedule.ErrorCode = null;

        var item = schedule.Items.FirstOrDefault(existing =>
            !existing.DeletedAt.HasValue &&
            existing.ItemId == postId &&
            string.Equals(existing.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            item.Status = string.Equals(finalStatus, PublishedStatus, StringComparison.OrdinalIgnoreCase)
                ? PublishingScheduleState.ItemStatusPublished
                : PublishingScheduleState.ItemStatusFailed;
            item.ErrorMessage = string.Equals(finalStatus, PublishedStatus, StringComparison.OrdinalIgnoreCase)
                ? null
                : "Post publishing failed.";
            item.LastExecutionAt = now;
            item.UpdatedAt = now;
        }

        var activeItems = schedule.Items.Where(existing => !existing.DeletedAt.HasValue).ToList();
        if (activeItems.Count > 0 && activeItems.All(existing =>
                string.Equals(existing.Status, PublishingScheduleState.ItemStatusPublished, StringComparison.OrdinalIgnoreCase)))
        {
            schedule.Status = PublishingScheduleState.StatusCompleted;
            schedule.ErrorMessage = null;
        }
        else if (activeItems.Any(existing =>
                     string.Equals(existing.Status, PublishingScheduleState.ItemStatusPublishing, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(existing.Status, PublishingScheduleState.ItemStatusScheduled, StringComparison.OrdinalIgnoreCase)))
        {
            schedule.Status = PublishingScheduleState.StatusPublishing;
        }
        else if (activeItems.Any(existing =>
                     string.Equals(existing.Status, PublishingScheduleState.ItemStatusFailed, StringComparison.OrdinalIgnoreCase)))
        {
            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = "PublishingSchedule.ItemFailed";
            schedule.ErrorMessage = "One or more schedule items failed to publish.";
        }

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
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

            var videoResources = presignedResources
                .Where(IsVideoResource)
                .ToList();
            var imageResources = presignedResources
                .Where(IsImageResource)
                .ToList();
            var normalizedPostType = NormalizePostType(post.Content?.PostType);

            if (videoResources.Count == 0 && imageResources.Count == 0)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(
                    new Error("TikTok.MissingMedia", "TikTok publishing requires at least one image or video."));
            }

            if (normalizedPostType == "reels")
            {
                if (videoResources.Count != 1 || imageResources.Count > 0)
                {
                    return Result.Failure<IReadOnlyList<(string, string)>>(
                        new Error("TikTok.ReelSingleVideo", "TikTok reels require exactly one video."));
                }

                var publishResult = await _tikTokPublishService.PublishAsync(
                    new TikTokPublishRequest(
                        AccessToken: accessToken,
                        OpenId: openId,
                        Caption: caption,
                        Media: new TikTokPublishMedia(
                            videoResources[0].PresignedUrl,
                            videoResources[0].ContentType ?? videoResources[0].ResourceType),
                        IsPrivate: isPrivate),
                    cancellationToken);

                if (publishResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<(string, string)>>(publishResult.Error);
                }

                return Result.Success<IReadOnlyList<(string, string)>>(
                    new[] { (publishResult.Value.OpenId, publishResult.Value.PublishId) });
            }

            var tikTokResults = new List<(string OpenId, string PublishId)>();

            foreach (var videoResource in videoResources)
            {
                var publishResult = await _tikTokPublishService.PublishAsync(
                    new TikTokPublishRequest(
                        AccessToken: accessToken,
                        OpenId: openId,
                        Caption: caption,
                        Media: new TikTokPublishMedia(
                            videoResource.PresignedUrl,
                            videoResource.ContentType ?? videoResource.ResourceType),
                        IsPrivate: isPrivate),
                    cancellationToken);

                if (publishResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<(string, string)>>(publishResult.Error);
                }

                tikTokResults.Add((publishResult.Value.OpenId, publishResult.Value.PublishId));
            }

            var imageUrls = imageResources
                .Select(r => r.PresignedUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToList();

            if (imageUrls.Count > 0)
            {
                var carouselResult = await _tikTokPublishService.PublishCarouselAsync(
                    new TikTokCarouselPublishRequest(
                        AccessToken: accessToken,
                        OpenId: openId,
                        Caption: caption,
                        ImageUrls: imageUrls,
                        IsPrivate: isPrivate),
                    cancellationToken);

                if (carouselResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<(string, string)>>(carouselResult.Error);
                }

                tikTokResults.Add((carouselResult.Value.OpenId, carouselResult.Value.PublishId));
            }

            return Result.Success<IReadOnlyList<(string, string)>>(tikTokResults);
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
                        .ToList(),
                    PostType: post.Content?.PostType),
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

            var publishResult = await _instagramPublishService.PublishAsync(
                new InstagramPublishRequest(
                    AccessToken: instagramAccessToken,
                    InstagramUserId: instagramUserId,
                    Caption: caption,
                    Media: presignedResources
                        .Select(resource => new InstagramPublishMedia(
                            resource.PresignedUrl,
                            resource.ContentType ?? resource.ResourceType))
                        .ToList(),
                    PostType: post.Content?.PostType),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<(string, string)>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<(string, string)>>(
                new[] { (publishResult.Value.InstagramUserId, publishResult.Value.PostId) });
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

        var media = presignedResources
            .Select(resource => new ThreadsPublishMedia(
                resource.PresignedUrl,
                resource.ContentType ?? resource.ResourceType))
            .ToList();

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

    private static bool IsVideoResource(UserResourcePresignResult resource)
    {
        var type = resource.ContentType ?? resource.ResourceType;
        if (!string.IsNullOrWhiteSpace(type) &&
            (type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(type, "video", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return HasExtension(resource.PresignedUrl, ".mp4", ".mov", ".m4v", ".webm");
    }

    private static bool IsImageResource(UserResourcePresignResult resource)
    {
        var type = resource.ContentType ?? resource.ResourceType;
        if (!string.IsNullOrWhiteSpace(type) &&
            (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(type, "image", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return HasExtension(resource.PresignedUrl, ".jpg", ".jpeg", ".png", ".gif", ".webp");
    }

    private static bool HasExtension(string? url, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var cleanUrl = url;
        var queryIndex = cleanUrl.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex > 0)
        {
            cleanUrl = cleanUrl[..queryIndex];
        }

        var extension = System.IO.Path.GetExtension(cleanUrl).ToLowerInvariant();
        return extensions.Contains(extension, StringComparer.Ordinal);
    }

    private static string NormalizePostType(string? postType)
    {
        var normalized = (postType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "reel" or "reels" or "video" ? "reels" : "posts";
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

    private static bool RequiresResources(string? platform, string? postType)
    {
        if (string.Equals(platform, ThreadsType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(platform, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedPostType = (postType ?? string.Empty).Trim().ToLowerInvariant();
            return normalizedPostType is "reel" or "reels" or "video";
        }

        return true;
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
