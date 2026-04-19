using Application.Abstractions.Resources;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Posts;

public sealed class PostResponseBuilder
{
    private const string UnknownUsername = "unknown";

    private readonly IUserResourceService _userResourceService;
    private readonly IPostPublicationRepository _postPublicationRepository;

    public PostResponseBuilder(
        IUserResourceService userResourceService,
        IPostPublicationRepository postPublicationRepository)
    {
        _userResourceService = userResourceService;
        _postPublicationRepository = postPublicationRepository;
    }

    public async Task<IReadOnlyList<PostResponse>> BuildManyAsync(
        Guid userId,
        IReadOnlyList<Post> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return Array.Empty<PostResponse>();
        }

        var resourceIds = posts
            .SelectMany(GetResourceIds)
            .Distinct()
            .ToList();

        var resourcesById = new Dictionary<Guid, UserResourcePresignResult>();
        if (resourceIds.Count > 0)
        {
            var presignResult = await _userResourceService.GetPresignedResourcesAsync(userId, resourceIds, cancellationToken);
            if (presignResult.IsSuccess)
            {
                resourcesById = presignResult.Value.ToDictionary(item => item.ResourceId, item => item);
            }
        }

        var authorIds = posts
            .Select(post => post.UserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var authorsById = authorIds.ToDictionary(id => id, CreateFallbackAuthor);
        if (authorIds.Count > 0)
        {
            var authorsResult = await _userResourceService.GetPublicUserProfilesByIdsAsync(authorIds, cancellationToken);
            if (authorsResult.IsSuccess)
            {
                authorsById = authorIds.ToDictionary(
                    id => id,
                    id => authorsResult.Value.TryGetValue(id, out var author)
                        ? author
                        : CreateFallbackAuthor(id));
            }
        }

        var publications = await _postPublicationRepository.GetByPostIdsAsync(
            posts.Select(post => post.Id).ToList(),
            cancellationToken);

        var publicationsByPostId = publications
            .GroupBy(publication => publication.PostId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PostPublication>)group.ToList());

        return posts
            .Select(post => Build(post, resourcesById, publicationsByPostId, authorsById))
            .ToList();
    }

    public async Task<PostResponse> BuildAsync(Guid userId, Post post, CancellationToken cancellationToken)
    {
        var responses = await BuildManyAsync(userId, new[] { post }, cancellationToken);
        return responses[0];
    }

    private static PostResponse Build(
        Post post,
        IReadOnlyDictionary<Guid, UserResourcePresignResult> resourcesById,
        IReadOnlyDictionary<Guid, IReadOnlyList<PostPublication>> publicationsByPostId,
        IReadOnlyDictionary<Guid, PublicUserProfileResult> authorsById)
    {
        var media = GetResourceIds(post)
            .Where(resourcesById.ContainsKey)
            .Select(resourceId =>
            {
                var resource = resourcesById[resourceId];
                return new PostMediaResponse(
                    resource.ResourceId,
                    resource.PresignedUrl,
                    resource.ContentType,
                    resource.ResourceType);
            })
            .ToList();

        var publications = publicationsByPostId.TryGetValue(post.Id, out var postPublications)
            ? postPublications
                .Select(publication => new PostPublicationResponse(
                    publication.Id,
                    publication.SocialMediaId,
                    publication.SocialMediaType,
                    publication.DestinationOwnerId,
                    publication.ExternalContentId,
                    publication.ExternalContentIdType,
                    publication.ContentType,
                    publication.PublishStatus,
                    publication.PublishedAt,
                    publication.CreatedAt))
                .ToList()
            : new List<PostPublicationResponse>();

        var author = authorsById.TryGetValue(post.UserId, out var authorProfile)
            ? authorProfile
            : CreateFallbackAuthor(post.UserId);

        return new PostResponse(
            Id: post.Id,
            UserId: post.UserId,
            Username: author.Username,
            AvatarUrl: author.AvatarUrl,
            WorkspaceId: post.WorkspaceId,
            SocialMediaId: post.SocialMediaId,
            Title: post.Title,
            Content: post.Content,
            Status: post.Status,
            IsPublished: publications.Any(publication =>
                string.Equals(publication.PublishStatus, "published", StringComparison.OrdinalIgnoreCase)),
            Media: media,
            Publications: publications,
            CreatedAt: post.CreatedAt,
            UpdatedAt: post.UpdatedAt);
    }

    private static PublicUserProfileResult CreateFallbackAuthor(Guid userId)
    {
        return new PublicUserProfileResult(userId, UnknownUsername, null, null);
    }

    private static IReadOnlyList<Guid> GetResourceIds(Post post)
    {
        if (post.Content?.ResourceList == null || post.Content.ResourceList.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var ids = new List<Guid>();
        foreach (var value in post.Content.ResourceList)
        {
            if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
            {
                ids.Add(parsed);
            }
        }

        return ids;
    }
}
