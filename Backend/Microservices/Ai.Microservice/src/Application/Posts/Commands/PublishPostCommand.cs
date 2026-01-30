using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
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
    Guid SocialMediaId) : IRequest<Result<PublishPostResponse>>;

public sealed class PublishPostCommandHandler
    : IRequestHandler<PublishPostCommand, Result<PublishPostResponse>>
{
    private const string FacebookType = "facebook";
    private const string PostsType = "posts";

    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookPublishService _facebookPublishService;

    public PublishPostCommandHandler(
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IUserSocialMediaService userSocialMediaService,
        IFacebookPublishService facebookPublishService)
    {
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _userSocialMediaService = userSocialMediaService;
        _facebookPublishService = facebookPublishService;
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

        if (!string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Post.InvalidSocialMedia", "Only Facebook social media accounts are supported for posts."));
        }

        var caption = post.Content?.Content?.Trim() ?? string.Empty;
        var mediaItems = presignResult.Value
            .Select(resource => new FacebookPublishMedia(resource.PresignedUrl, resource.ContentType ?? resource.ResourceType))
            .ToList();

        using var metadata = ParseMetadata(socialMedia.MetadataJson);
        var userAccessToken = GetMetadataValue(metadata, "user_access_token")
                              ?? GetMetadataValue(metadata, "access_token");

        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            return Result.Failure<PublishPostResponse>(
                new Error("Facebook.InvalidToken", "Access token not found in social media metadata."));
        }

        var pageId = GetMetadataValue(metadata, "page_id");
        var pageAccessToken = GetMetadataValue(metadata, "page_access_token");

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

        var publishResults = publishResult.Value
            .Select(result => new PublishPostDestinationResult(
                socialMedia.SocialMediaId,
                socialMedia.Type,
                result.PageId,
                result.PostId))
            .ToList();

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
