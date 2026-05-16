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
    private readonly IDraftPostTaskRepository? _draftPostTaskRepository;
    private readonly IRecommendPostRepository? _recommendPostRepository;

    public PostResponseBuilder(
        IUserResourceService userResourceService,
        IPostPublicationRepository postPublicationRepository,
        IDraftPostTaskRepository? draftPostTaskRepository = null,
        IRecommendPostRepository? recommendPostRepository = null)
    {
        _userResourceService = userResourceService;
        _postPublicationRepository = postPublicationRepository;
        _draftPostTaskRepository = draftPostTaskRepository;
        _recommendPostRepository = recommendPostRepository;
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

        var recommendationTasksByPostId = await BuildRecommendationTasksByPostIdAsync(posts, cancellationToken);
        var improveTasksByPostId = await BuildImproveTasksByPostIdAsync(posts, cancellationToken);

        return posts
            .Select(post => Build(post, resourcesById, publicationsByPostId, authorsById, recommendationTasksByPostId, improveTasksByPostId))
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
        IReadOnlyDictionary<Guid, PublicUserProfileResult> authorsById,
        IReadOnlyDictionary<Guid, DraftPostTask> recommendationTasksByPostId,
        IReadOnlyDictionary<Guid, RecommendPost> improveTasksByPostId)
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

        var schedule = post.ScheduleGroupId.HasValue && post.ScheduledAtUtc.HasValue
            ? new PostScheduleResponse(
                post.ScheduleGroupId.Value,
                post.ScheduledAtUtc.Value,
                post.ScheduleTimezone,
                post.ScheduledSocialMediaIds,
                post.ScheduledIsPrivate)
            : null;

        var hasRecommendationTask = recommendationTasksByPostId.TryGetValue(
            post.Id,
            out var recommendationTask);
        var recommendationStatus = recommendationTask?.Status;
        improveTasksByPostId.TryGetValue(post.Id, out var improveTask);
        var improveStatus = improveTask?.Status;
        var responseStatus = string.Equals(
            recommendationStatus,
            DraftPostTaskStatuses.Failed,
            StringComparison.OrdinalIgnoreCase)
                ? "failed"
                : post.Status;

        return new PostResponse(
            Id: post.Id,
            UserId: post.UserId,
            Username: author.Username,
            AvatarUrl: author.AvatarUrl,
            WorkspaceId: post.WorkspaceId,
            PostBuilderId: post.PostBuilderId,
            ChatSessionId: post.ChatSessionId,
            SocialMediaId: post.SocialMediaId,
            Platform: post.Platform,
            Title: post.Title,
            Content: post.Content,
            Status: responseStatus,
            Schedule: schedule,
            IsPublished: publications.Any(publication =>
                string.Equals(publication.PublishStatus, "published", StringComparison.OrdinalIgnoreCase)),
            Media: media,
            Publications: publications,
            CreatedAt: post.CreatedAt,
            UpdatedAt: post.UpdatedAt,
            IsAiRecommendedDraft: hasRecommendationTask,
            AiRecommendationCorrelationId: recommendationTask?.CorrelationId,
            AiRecommendationStatus: recommendationStatus,
            IsAiRecommendationDone: IsTerminalRecommendationStatus(recommendationStatus),
            AiRecommendationCompletedAt: recommendationTask?.CompletedAt,
            AiRecommendationErrorCode: recommendationTask?.ErrorCode,
            AiRecommendationErrorMessage: recommendationTask?.ErrorMessage,
            AiImproveRecommendPostId: improveTask?.Id,
            AiImproveCorrelationId: improveTask?.CorrelationId,
            AiImproveStatus: improveStatus,
            IsAiImproving: IsRunningImproveStatus(improveStatus),
            IsAiImproveDone: IsTerminalImproveStatus(improveStatus),
            AiImproveCompletedAt: improveTask?.CompletedAt,
            AiImproveErrorCode: improveTask?.ErrorCode,
            AiImproveErrorMessage: improveTask?.ErrorMessage);
    }

    private static PublicUserProfileResult CreateFallbackAuthor(Guid userId)
    {
        return new PublicUserProfileResult(userId, UnknownUsername, null, null);
    }

    private async Task<IReadOnlyDictionary<Guid, DraftPostTask>> BuildRecommendationTasksByPostIdAsync(
        IReadOnlyList<Post> posts,
        CancellationToken cancellationToken)
    {
        if (_draftPostTaskRepository is null)
        {
            return new Dictionary<Guid, DraftPostTask>();
        }

        var postIds = posts
            .Select(post => post.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (postIds.Count == 0)
        {
            return new Dictionary<Guid, DraftPostTask>();
        }

        var tasks = await _draftPostTaskRepository.GetByResultPostIdsAsync(postIds, cancellationToken);
        return tasks
            .Where(task => task.ResultPostId.HasValue)
            .GroupBy(task => task.ResultPostId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(task => task.CreatedAt)
                    .ThenByDescending(task => task.CorrelationId)
                    .First());
    }

    private async Task<IReadOnlyDictionary<Guid, RecommendPost>> BuildImproveTasksByPostIdAsync(
        IReadOnlyList<Post> posts,
        CancellationToken cancellationToken)
    {
        if (_recommendPostRepository is null)
        {
            return new Dictionary<Guid, RecommendPost>();
        }

        var postIds = posts
            .Select(post => post.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (postIds.Count == 0)
        {
            return new Dictionary<Guid, RecommendPost>();
        }

        var tasks = await _recommendPostRepository.GetByOriginalPostIdsAsync(postIds, cancellationToken);
        return tasks
            .GroupBy(task => task.OriginalPostId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(task => task.CreatedAt)
                    .ThenByDescending(task => task.CorrelationId)
                    .First());
    }

    private static bool IsTerminalRecommendationStatus(string? status)
    {
        return string.Equals(status, DraftPostTaskStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, DraftPostTaskStatuses.Failed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRunningImproveStatus(string? status)
    {
        return string.Equals(status, RecommendPostStatuses.Submitted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, RecommendPostStatuses.Processing, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalImproveStatus(string? status)
    {
        return string.Equals(status, RecommendPostStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, RecommendPostStatuses.Failed, StringComparison.OrdinalIgnoreCase);
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
