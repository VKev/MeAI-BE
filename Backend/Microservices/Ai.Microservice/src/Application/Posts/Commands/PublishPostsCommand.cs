using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record PublishPostsCommand(
    Guid UserId,
    IReadOnlyList<PublishPostTargetInput> Targets) : IRequest<Result<PublishPostsResponse>>;

public sealed record PublishPostTargetInput(
    Guid PostId,
    IReadOnlyList<Guid> SocialMediaIds,
    bool? IsPrivate = null);

public sealed class PublishPostsCommandHandler
    : IRequestHandler<PublishPostsCommand, Result<PublishPostsResponse>>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";
    private const string PostsType = "posts";

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookPublishService _facebookPublishService;
    private readonly IInstagramPublishService _instagramPublishService;
    private readonly ITikTokPublishService _tikTokPublishService;
    private readonly IThreadsPublishService _threadsPublishService;
    private readonly IBus _bus;

    public PublishPostsCommandHandler(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserResourceService userResourceService,
        IUserSocialMediaService userSocialMediaService,
        IFacebookPublishService facebookPublishService,
        IInstagramPublishService instagramPublishService,
        ITikTokPublishService tikTokPublishService,
        IThreadsPublishService threadsPublishService,
        IBus bus)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userResourceService = userResourceService;
        _userSocialMediaService = userSocialMediaService;
        _facebookPublishService = facebookPublishService;
        _instagramPublishService = instagramPublishService;
        _tikTokPublishService = tikTokPublishService;
        _threadsPublishService = threadsPublishService;
        _bus = bus;
    }

    public async Task<Result<PublishPostsResponse>> Handle(
        PublishPostsCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedTargetsResult = NormalizeTargets(request.Targets);
        if (normalizedTargetsResult.IsFailure)
        {
            return Result.Failure<PublishPostsResponse>(normalizedTargetsResult.Error);
        }

        var normalizedTargets = normalizedTargetsResult.Value;
        var socialMediaByIdResult = await GetSocialMediaByIdAsync(
            request.UserId,
            normalizedTargets,
            cancellationToken);

        if (socialMediaByIdResult.IsFailure)
        {
            return Result.Failure<PublishPostsResponse>(socialMediaByIdResult.Error);
        }

        var preparedTargetsResult = await PrepareTargetsAsync(
            request.UserId,
            normalizedTargets,
            socialMediaByIdResult.Value,
            cancellationToken);

        if (preparedTargetsResult.IsFailure)
        {
            return Result.Failure<PublishPostsResponse>(preparedTargetsResult.Error);
        }

        var publishedPosts = new List<PublishPostResponse>(preparedTargetsResult.Value.Count);

        foreach (var target in preparedTargetsResult.Value)
        {
            var destinationResults = new List<PublishPostDestinationResult>();

            foreach (var socialMedia in target.SocialMedias)
            {
                var publishResult = await PublishToSocialMediaAsync(
                    target,
                    socialMedia,
                    cancellationToken);

                if (publishResult.IsFailure)
                {
                    await NotifyPublicationFailedAsync(
                        request.UserId,
                        target.Post,
                        socialMedia,
                        publishResult.Error,
                        cancellationToken);

                    return Result.Failure<PublishPostsResponse>(publishResult.Error);
                }

                destinationResults.AddRange(publishResult.Value);
                await PersistPublishResultsAsync(
                    target.Post,
                    publishResult.Value,
                    cancellationToken);
            }

            await NotifyPublicationCompletedAsync(
                request.UserId,
                target.Post,
                destinationResults,
                cancellationToken);

            publishedPosts.Add(new PublishPostResponse(
                target.Post.Id,
                target.Post.Status ?? "published",
                destinationResults));
        }

        return Result.Success(new PublishPostsResponse(publishedPosts));
    }

    private async Task<Result<IReadOnlyDictionary<Guid, UserSocialMediaResult>>> GetSocialMediaByIdAsync(
        Guid userId,
        IReadOnlyList<PublishPostTargetInput> targets,
        CancellationToken cancellationToken)
    {
        var allSocialMediaIds = targets
            .SelectMany(target => target.SocialMediaIds)
            .Distinct()
            .ToList();

        var socialMediasResult = await _userSocialMediaService.GetSocialMediasAsync(
            userId,
            allSocialMediaIds,
            cancellationToken);

        if (socialMediasResult.IsFailure)
        {
            return Result.Failure<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(socialMediasResult.Error);
        }

        var socialMediaById = socialMediasResult.Value.ToDictionary(item => item.SocialMediaId);

        foreach (var socialMediaId in allSocialMediaIds)
        {
            if (!socialMediaById.ContainsKey(socialMediaId))
            {
                return Result.Failure<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(
                    new Error("SocialMedia.NotFound", "Social media account not found."));
            }
        }

        foreach (var socialMedia in socialMediaById.Values)
        {
            if (!string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(
                    new Error(
                        "Post.InvalidSocialMedia",
                        "Only TikTok, Facebook, Instagram, or Threads social media accounts are supported for posts."));
            }
        }

        return Result.Success<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(socialMediaById);
    }

    private async Task<Result<IReadOnlyList<PreparedPublishPostTarget>>> PrepareTargetsAsync(
        Guid userId,
        IReadOnlyList<PublishPostTargetInput> targets,
        IReadOnlyDictionary<Guid, UserSocialMediaResult> socialMediaById,
        CancellationToken cancellationToken)
    {
        var preparedTargets = new List<PreparedPublishPostTarget>(targets.Count);

        foreach (var target in targets)
        {
            var post = await _postRepository.GetByIdForUpdateAsync(target.PostId, cancellationToken);
            if (post == null || post.DeletedAt.HasValue)
            {
                return Result.Failure<IReadOnlyList<PreparedPublishPostTarget>>(
                    new Error("Post.NotFound", "Post not found."));
            }

            if (post.UserId != userId)
            {
                return Result.Failure<IReadOnlyList<PreparedPublishPostTarget>>(
                    new Error("Post.Unauthorized", "You are not authorized to publish this post."));
            }

            if (!post.WorkspaceId.HasValue)
            {
                return Result.Failure<IReadOnlyList<PreparedPublishPostTarget>>(PostErrors.WorkspaceIdRequired);
            }

            var postType = post.Content?.PostType ?? PostsType;
            if (!string.Equals(postType, PostsType, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<IReadOnlyList<PreparedPublishPostTarget>>(
                    new Error("Post.UnsupportedType", "Only 'posts' can be published at the moment."));
            }

            var socialMedias = target.SocialMediaIds
                .Select(id => socialMediaById[id])
                .ToList();

            var resourceIds = ExtractResourceIds(post.Content);
            var requiresResources = socialMedias.Any(item =>
                !string.Equals(item.Type, ThreadsType, StringComparison.OrdinalIgnoreCase));

            IReadOnlyList<UserResourcePresignResult> presignedResources = Array.Empty<UserResourcePresignResult>();

            if (requiresResources || resourceIds.Count > 0)
            {
                if (resourceIds.Count == 0)
                {
                    return Result.Failure<IReadOnlyList<PreparedPublishPostTarget>>(
                        new Error("Post.MissingResources", "This post has no resources to publish."));
                }

                var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                    userId,
                    resourceIds,
                    cancellationToken);

                if (presignResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<PreparedPublishPostTarget>>(presignResult.Error);
                }

                presignedResources = presignResult.Value;
            }

            preparedTargets.Add(new PreparedPublishPostTarget(
                post,
                socialMedias,
                presignedResources,
                target.IsPrivate));
        }

        return Result.Success<IReadOnlyList<PreparedPublishPostTarget>>(preparedTargets);
    }

    private async Task<Result<IReadOnlyList<PublishPostDestinationResult>>> PublishToSocialMediaAsync(
        PreparedPublishPostTarget target,
        UserSocialMediaResult socialMedia,
        CancellationToken cancellationToken)
    {
        var caption = target.Post.Content?.Content?.Trim() ?? string.Empty;
        using var metadata = ParseMetadata(socialMedia.MetadataJson);

        if (string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            if (target.PresignedResources.Count == 0)
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("TikTok.MissingMedia", "TikTok publishing requires at least one video."));
            }

            if (target.PresignedResources.Count > 1)
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("TikTok.UnsupportedMedia", "TikTok publishing currently supports only one video."));
            }

            var accessToken = GetMetadataValue(metadata, "access_token");
            var openId = GetMetadataValue(metadata, "open_id");

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("TikTok.InvalidToken", "Access token not found in social media metadata."));
            }

            if (string.IsNullOrWhiteSpace(openId))
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("TikTok.InvalidAccount", "TikTok open_id is missing in social media metadata."));
            }

            var resource = target.PresignedResources[0];
            var publishResult = await _tikTokPublishService.PublishAsync(
                new TikTokPublishRequest(
                    AccessToken: accessToken,
                    OpenId: openId,
                    Caption: caption,
                    Media: new TikTokPublishMedia(
                        resource.PresignedUrl,
                        resource.ContentType ?? resource.ResourceType),
                    IsPrivate: target.IsPrivate),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<PublishPostDestinationResult>>(
            [
                new PublishPostDestinationResult(
                    socialMedia.SocialMediaId,
                    socialMedia.Type,
                    publishResult.Value.OpenId,
                    publishResult.Value.PublishId)
            ]);
        }

        if (string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var userAccessToken = GetMetadataValue(metadata, "user_access_token")
                                  ?? GetMetadataValue(metadata, "access_token");

            if (string.IsNullOrWhiteSpace(userAccessToken))
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
            }

            var publishResult = await _facebookPublishService.PublishAsync(
                new FacebookPublishRequest(
                    UserAccessToken: userAccessToken,
                    PageId: GetMetadataValue(metadata, "page_id"),
                    PageAccessToken: GetMetadataValue(metadata, "page_access_token"),
                    Message: caption,
                    Media: target.PresignedResources
                        .Select(resource => new FacebookPublishMedia(
                            resource.PresignedUrl,
                            resource.ContentType ?? resource.ResourceType))
                        .ToList()),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<PublishPostDestinationResult>>(
                publishResult.Value
                    .Select(result => new PublishPostDestinationResult(
                        socialMedia.SocialMediaId,
                        socialMedia.Type,
                        result.PageId,
                        result.PostId))
                    .ToList());
        }

        if (string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase))
        {
            if (target.PresignedResources.Count != 1)
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("Instagram.UnsupportedMedia", "Instagram publishing currently supports only one media item."));
            }

            var instagramUserId = GetMetadataValue(metadata, "instagram_business_account_id")
                                  ?? GetMetadataValue(metadata, "user_id");

            if (string.IsNullOrWhiteSpace(instagramUserId))
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("Instagram.InvalidAccount", "Instagram business account id is missing in social media metadata."));
            }

            var instagramAccessToken = GetMetadataValue(metadata, "access_token")
                                       ?? GetMetadataValue(metadata, "user_access_token");

            if (string.IsNullOrWhiteSpace(instagramAccessToken))
            {
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                    new Error("Instagram.InvalidToken", "Access token not found in social media metadata."));
            }

            var resource = target.PresignedResources[0];
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
                return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(publishResult.Error);
            }

            return Result.Success<IReadOnlyList<PublishPostDestinationResult>>(
            [
                new PublishPostDestinationResult(
                    socialMedia.SocialMediaId,
                    socialMedia.Type,
                    publishResult.Value.InstagramUserId,
                    publishResult.Value.PostId)
            ]);
        }

        if (target.PresignedResources.Count > 1)
        {
            return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                new Error("Threads.UnsupportedMedia", "Threads publishing currently supports one media item at a time."));
        }

        var threadsUserId = GetMetadataValue(metadata, "user_id");
        if (string.IsNullOrWhiteSpace(threadsUserId))
        {
            return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                new Error("Threads.InvalidAccount", "Threads user id is missing in social media metadata."));
        }

        var threadsAccessToken = GetMetadataValue(metadata, "access_token");
        if (string.IsNullOrWhiteSpace(threadsAccessToken))
        {
            return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(
                new Error("Threads.InvalidToken", "Access token not found in social media metadata."));
        }

        ThreadsPublishMedia? media = null;
        if (target.PresignedResources.Count == 1)
        {
            var resource = target.PresignedResources[0];
            media = new ThreadsPublishMedia(
                resource.PresignedUrl,
                resource.ContentType ?? resource.ResourceType);
        }

        var threadsPublishResult = await _threadsPublishService.PublishAsync(
            new ThreadsPublishRequest(
                AccessToken: threadsAccessToken,
                ThreadsUserId: threadsUserId,
                Text: caption,
                Media: media),
            cancellationToken);

        if (threadsPublishResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PublishPostDestinationResult>>(threadsPublishResult.Error);
        }

        return Result.Success<IReadOnlyList<PublishPostDestinationResult>>(
        [
            new PublishPostDestinationResult(
                socialMedia.SocialMediaId,
                socialMedia.Type,
                threadsPublishResult.Value.ThreadsUserId,
                threadsPublishResult.Value.PostId)
        ]);
    }

    private async Task NotifyPublicationCompletedAsync(
        Guid userId,
        Post post,
        IReadOnlyList<PublishPostDestinationResult> publishResults,
        CancellationToken cancellationToken)
    {
        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                userId,
                NotificationTypes.AiPostPublishCompleted,
                "Post published",
                publishResults.Count == 1
                    ? "Your post was published to 1 destination."
                    : $"Your post was published to {publishResults.Count} destinations.",
                new
                {
                    postId = post.Id,
                    post.Status,
                    post.WorkspaceId,
                    destinations = publishResults.Select(result => new
                    {
                        result.SocialMediaId,
                        result.SocialMediaType,
                        result.PageId,
                        result.ExternalPostId
                    }).ToList()
                },
                userId),
            cancellationToken);
    }

    private async Task NotifyPublicationFailedAsync(
        Guid userId,
        Post post,
        UserSocialMediaResult socialMedia,
        Error error,
        CancellationToken cancellationToken)
    {
        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                userId,
                NotificationTypes.AiPostPublishFailed,
                "Post publish failed",
                error.Description,
                new
                {
                    postId = post.Id,
                    post.Status,
                    post.WorkspaceId,
                    socialMediaId = socialMedia.SocialMediaId,
                    socialMediaType = socialMedia.Type,
                    error = new
                    {
                        error.Code,
                        error.Description
                    }
                },
                userId),
            cancellationToken);
    }

    private async Task PersistPublishResultsAsync(
        Post post,
        IReadOnlyList<PublishPostDestinationResult> publishResults,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        post.Status = "published";
        post.UpdatedAt = now;

        var publications = publishResults.Select(result => new PostPublication
        {
            Id = Guid.CreateVersion7(),
            PostId = post.Id,
            WorkspaceId = post.WorkspaceId!.Value,
            SocialMediaId = result.SocialMediaId,
            SocialMediaType = result.SocialMediaType,
            DestinationOwnerId = result.PageId,
            ExternalContentId = result.ExternalPostId,
            ExternalContentIdType = string.Equals(result.SocialMediaType, TikTokType, StringComparison.OrdinalIgnoreCase)
                ? "publish_id"
                : "post_id",
            ContentType = post.Content?.PostType ?? PostsType,
            PublishStatus = "published",
            PublishedAt = now,
            CreatedAt = now
        }).ToList();

        await _postPublicationRepository.AddRangeAsync(publications, cancellationToken);
        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);
    }

    private static Result<IReadOnlyList<PublishPostTargetInput>> NormalizeTargets(
        IReadOnlyList<PublishPostTargetInput>? targets)
    {
        if (targets is null || targets.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                new Error("Post.PublishMissingTargets", "At least one publish target is required."));
        }

        var normalizedByPostId = new Dictionary<Guid, NormalizedPublishTarget>();

        foreach (var target in targets)
        {
            if (target.PostId == Guid.Empty)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishMissingPostId", "Each publish target must include a valid postId."));
            }

            var socialMediaIds = target.SocialMediaIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (socialMediaIds.Count == 0)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishMissingSocialMedia", "Each publish target must include at least one social media id."));
            }

            if (normalizedByPostId.TryGetValue(target.PostId, out var existing) &&
                existing.IsPrivate.HasValue &&
                target.IsPrivate.HasValue &&
                existing.IsPrivate.Value != target.IsPrivate.Value)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishPrivacyConflict", "Conflicting isPrivate values were provided for the same post."));
            }

            if (!normalizedByPostId.TryGetValue(target.PostId, out existing))
            {
                existing = new NormalizedPublishTarget(target.IsPrivate);
                normalizedByPostId[target.PostId] = existing;
            }
            else if (!existing.IsPrivate.HasValue && target.IsPrivate.HasValue)
            {
                existing.IsPrivate = target.IsPrivate.Value;
            }

            foreach (var socialMediaId in socialMediaIds)
            {
                existing.SocialMediaIds.Add(socialMediaId);
            }
        }

        return Result.Success<IReadOnlyList<PublishPostTargetInput>>(
            normalizedByPostId
                .Select(item => new PublishPostTargetInput(
                    item.Key,
                    item.Value.SocialMediaIds.ToList(),
                    item.Value.IsPrivate))
                .ToList());
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

    private sealed record PreparedPublishPostTarget(
        Post Post,
        IReadOnlyList<UserSocialMediaResult> SocialMedias,
        IReadOnlyList<UserResourcePresignResult> PresignedResources,
        bool? IsPrivate);

    private sealed class NormalizedPublishTarget
    {
        public NormalizedPublishTarget(bool? isPrivate)
        {
            IsPrivate = isPrivate;
        }

        public HashSet<Guid> SocialMediaIds { get; } = [];

        public bool? IsPrivate { get; set; }
    }
}
