using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.TikTok;
using Application.Abstractions.Threads;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record PublishPostCommand(
    Guid UserId,
    Guid PostId,
    Guid SocialMediaId,
    bool? IsPrivate = null) : IRequest<Result<PublishPostResponse>>;

public sealed class PublishPostCommandHandler
    : IRequestHandler<PublishPostCommand, Result<PublishPostResponse>>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";
    private const string PostsType = "posts";

    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookPublishService _facebookPublishService;
    private readonly IInstagramPublishService _instagramPublishService;
    private readonly ITikTokPublishService _tikTokPublishService;
    private readonly IThreadsPublishService _threadsPublishService;

    public PublishPostCommandHandler(
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IUserSocialMediaService userSocialMediaService,
        IFacebookPublishService facebookPublishService,
        IInstagramPublishService instagramPublishService,
        ITikTokPublishService tikTokPublishService,
        IThreadsPublishService threadsPublishService)
    {
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _userSocialMediaService = userSocialMediaService;
        _facebookPublishService = facebookPublishService;
        _instagramPublishService = instagramPublishService;
        _tikTokPublishService = tikTokPublishService;
        _threadsPublishService = threadsPublishService;
    }

    public async Task<Result<PublishPostResponse>> Handle(
        PublishPostCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SocialMediaId == Guid.Empty)
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Post.PublishMissingSocialMedia", "Social media id is required."));
        }

        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);
        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Post.NotFound", "Post not found."));
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Post.Unauthorized", "You are not authorized to publish this post."));
        }

        var postType = post.Content?.PostType ?? PostsType;
        if (!string.Equals(postType, PostsType, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Post.UnsupportedType", "Only 'posts' can be published at the moment."));
        }

        var resourceIds = ExtractResourceIds(post.Content);
        var socialMediasResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediasResult.IsFailure)
        {
            return Result.Failure<PublishPostResponse>(socialMediasResult.Error);
        }

        var socialMedia = socialMediasResult.Value.FirstOrDefault();
        if (socialMedia == null)
        {
            return Result.Failure<PublishPostResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        if (!string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Post.InvalidSocialMedia", "Only TikTok, Facebook, Instagram, or Threads social media accounts are supported for posts."));
        }

        var caption = post.Content?.Content?.Trim() ?? string.Empty;
        var presignedResources = Array.Empty<UserResourcePresignResult>();

        if (!string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase) ||
            resourceIds.Count > 0)
        {
            if (resourceIds.Count == 0)
            {
                return Result.Failure<PublishPostResponse>(
                    new Error("Post.MissingResources", "This post has no resources to publish."));
            }

            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                resourceIds,
                cancellationToken);

            if (presignResult.IsFailure)
            {
                return Result.Failure<PublishPostResponse>(presignResult.Error);
            }

            presignedResources = presignResult.Value.ToArray();
        }

        using var metadata = ParseMetadata(socialMedia.MetadataJson);

        List<PublishPostDestinationResult> publishResults;

        if (string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase))
        {
            if (presignedResources.Length == 0)
            {
                return Result.Failure<PublishPostResponse>(
                    new Error("TikTok.MissingMedia", "TikTok publishing requires at least one video."));
            }

            if (presignedResources.Length > 1)
            {
                return Result.Failure<PublishPostResponse>(
                    new Error("TikTok.UnsupportedMedia", "TikTok publishing currently supports only one video."));
            }

            var accessToken = GetMetadataValue(metadata, "access_token");
            var openId = GetMetadataValue(metadata, "open_id");

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Result.Failure<PublishPostResponse>(
                    new Error("TikTok.InvalidToken", "Access token not found in social media metadata."));
            }

            if (string.IsNullOrWhiteSpace(openId))
            {
                return Result.Failure<PublishPostResponse>(
                    new Error("TikTok.InvalidAccount", "TikTok open_id is missing in social media metadata."));
            }

            var publishResult = await _tikTokPublishService.PublishAsync(
                new TikTokPublishRequest(
                    AccessToken: accessToken,
                    OpenId: openId,
                    Caption: caption,
                    Media: new TikTokPublishMedia(
                        presignedResources[0].PresignedUrl,
                        presignedResources[0].ContentType ?? presignedResources[0].ResourceType),
                    IsPrivate: request.IsPrivate),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<PublishPostResponse>(publishResult.Error);
            }

            publishResults = new List<PublishPostDestinationResult>
            {
                new(
                    socialMedia.SocialMediaId,
                    socialMedia.Type,
                    publishResult.Value.OpenId,
                    publishResult.Value.PublishId)
            };
        }
        else if (string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            var pageId = GetMetadataValue(metadata, "page_id");
            var pageAccessToken = GetMetadataValue(metadata, "page_access_token");
            var mediaItems = presignedResources
                .Select(resource => new FacebookPublishMedia(resource.PresignedUrl, resource.ContentType ?? resource.ResourceType))
                .ToList();

            var userAccessToken = GetMetadataValue(metadata, "user_access_token")
                                  ?? GetMetadataValue(metadata, "access_token");

            if (string.IsNullOrWhiteSpace(userAccessToken))
            {
                return Result.Failure<PublishPostResponse>(
                    new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
            }

            var publishResult = await _facebookPublishService.PublishAsync(
                new FacebookPublishRequest(
                    UserAccessToken: userAccessToken,
                    PageId: pageId,
                    PageAccessToken: pageAccessToken,
                    Message: caption,
                    Media: mediaItems),
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<PublishPostResponse>(publishResult.Error);
            }

            publishResults = publishResult.Value
                .Select(result => new PublishPostDestinationResult(
                    socialMedia.SocialMediaId,
                    socialMedia.Type,
                    result.PageId,
                    result.PostId))
                .ToList();
        }
        else
        {
            if (string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase))
            {
                var mediaItems = presignedResources
                    .Select(resource => new InstagramPublishMedia(resource.PresignedUrl, resource.ContentType ?? resource.ResourceType))
                    .ToList();

                if (mediaItems.Count != 1)
                {
                    return Result.Failure<PublishPostResponse>(
                        new Error("Instagram.UnsupportedMedia", "Instagram publishing currently supports only one media item."));
                }

                var instagramUserId = GetMetadataValue(metadata, "instagram_business_account_id")
                                      ?? GetMetadataValue(metadata, "user_id");

                if (string.IsNullOrWhiteSpace(instagramUserId))
                {
                    return Result.Failure<PublishPostResponse>(
                        new Error("Instagram.InvalidAccount", "Instagram business account id is missing in social media metadata."));
                }

                var instagramAccessToken = GetMetadataValue(metadata, "access_token")
                                           ?? GetMetadataValue(metadata, "user_access_token");

                if (string.IsNullOrWhiteSpace(instagramAccessToken))
                {
                    return Result.Failure<PublishPostResponse>(
                        new Error("Instagram.InvalidToken", "Access token not found in social media metadata."));
                }

                var publishResult = await _instagramPublishService.PublishAsync(
                    new InstagramPublishRequest(
                        AccessToken: instagramAccessToken,
                        InstagramUserId: instagramUserId,
                        Caption: caption,
                        Media: mediaItems[0]),
                    cancellationToken);

                if (publishResult.IsFailure)
                {
                    return Result.Failure<PublishPostResponse>(publishResult.Error);
                }

                publishResults = new List<PublishPostDestinationResult>
                {
                    new(
                        socialMedia.SocialMediaId,
                        socialMedia.Type,
                        publishResult.Value.InstagramUserId,
                        publishResult.Value.PostId)
                };
            }
            else
            {
                if (presignedResources.Length > 1)
                {
                    return Result.Failure<PublishPostResponse>(
                        new Error("Threads.UnsupportedMedia", "Threads publishing currently supports one media item at a time."));
                }

                var threadsUserId = GetMetadataValue(metadata, "user_id");

                if (string.IsNullOrWhiteSpace(threadsUserId))
                {
                    return Result.Failure<PublishPostResponse>(
                        new Error("Threads.InvalidAccount", "Threads user id is missing in social media metadata."));
                }

                var threadsAccessToken = GetMetadataValue(metadata, "access_token");

                if (string.IsNullOrWhiteSpace(threadsAccessToken))
                {
                    return Result.Failure<PublishPostResponse>(
                        new Error("Threads.InvalidToken", "Access token not found in social media metadata."));
                }

                ThreadsPublishMedia? threadsMedia = null;
                if (presignedResources.Length == 1)
                {
                    var resource = presignedResources[0];
                    threadsMedia = new ThreadsPublishMedia(
                        resource.PresignedUrl,
                        resource.ContentType ?? resource.ResourceType);
                }

                var publishResult = await _threadsPublishService.PublishAsync(
                    new ThreadsPublishRequest(
                        AccessToken: threadsAccessToken,
                        ThreadsUserId: threadsUserId,
                        Text: caption,
                        Media: threadsMedia),
                    cancellationToken);

                if (publishResult.IsFailure)
                {
                    return Result.Failure<PublishPostResponse>(publishResult.Error);
                }

                publishResults = new List<PublishPostDestinationResult>
                {
                    new(
                        socialMedia.SocialMediaId,
                        socialMedia.Type,
                        publishResult.Value.ThreadsUserId,
                        publishResult.Value.PostId)
                };
            }
        }

        post.Status = "published";
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new PublishPostResponse(
            post.Id,
            post.Status ?? "published",
            publishResults));
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
