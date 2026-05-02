using Application.Abstractions.Resources;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

#pragma warning disable SA1402

public sealed record ListUserPostBuildersQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int Limit) : IRequest<Result<IReadOnlyList<PostBuilderSummaryResponse>>>;

public sealed record ListWorkspacePostBuildersQuery(
    Guid WorkspaceId,
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int Limit) : IRequest<Result<IReadOnlyList<PostBuilderSummaryResponse>>>;

public sealed class ListUserPostBuildersQueryHandler
    : IRequestHandler<ListUserPostBuildersQuery, Result<IReadOnlyList<PostBuilderSummaryResponse>>>
{
    private readonly IPostBuilderRepository _repository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserResourceService _userResourceService;

    public ListUserPostBuildersQueryHandler(
        IPostBuilderRepository repository,
        IPostPublicationRepository postPublicationRepository,
        IUserResourceService userResourceService)
    {
        _repository = repository;
        _postPublicationRepository = postPublicationRepository;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<PostBuilderSummaryResponse>>> Handle(
        ListUserPostBuildersQuery request,
        CancellationToken cancellationToken)
    {
        var builders = await _repository.GetByUserAsync(
            request.UserId,
            request.CursorCreatedAt,
            request.CursorId,
            NormalizeLimit(request.Limit),
            cancellationToken);

        var summaries = await PostBuilderSummaryHelper.BuildAsync(
            request.UserId,
            builders,
            _userResourceService,
            _postPublicationRepository,
            cancellationToken);

        return Result.Success(summaries);
    }

    private static int NormalizeLimit(int limit) => limit <= 0 ? 12 : Math.Min(limit, 50);
}

public sealed class ListWorkspacePostBuildersQueryHandler
    : IRequestHandler<ListWorkspacePostBuildersQuery, Result<IReadOnlyList<PostBuilderSummaryResponse>>>
{
    private readonly IPostBuilderRepository _repository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserResourceService _userResourceService;

    public ListWorkspacePostBuildersQueryHandler(
        IPostBuilderRepository repository,
        IPostPublicationRepository postPublicationRepository,
        IUserResourceService userResourceService)
    {
        _repository = repository;
        _postPublicationRepository = postPublicationRepository;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<PostBuilderSummaryResponse>>> Handle(
        ListWorkspacePostBuildersQuery request,
        CancellationToken cancellationToken)
    {
        var builders = await _repository.GetByWorkspaceAsync(
            request.WorkspaceId,
            request.UserId,
            request.CursorCreatedAt,
            request.CursorId,
            NormalizeLimit(request.Limit),
            cancellationToken);

        var summaries = await PostBuilderSummaryHelper.BuildAsync(
            request.UserId,
            builders,
            _userResourceService,
            _postPublicationRepository,
            cancellationToken);

        return Result.Success(summaries);
    }

    private static int NormalizeLimit(int limit) => limit <= 0 ? 12 : Math.Min(limit, 50);
}

internal static class PostBuilderSummaryHelper
{
    public static async Task<IReadOnlyList<PostBuilderSummaryResponse>> BuildAsync(
        Guid userId,
        IReadOnlyList<PostBuilder> builders,
        IUserResourceService userResourceService,
        IPostPublicationRepository postPublicationRepository,
        CancellationToken cancellationToken)
    {
        if (builders.Count == 0)
        {
            return Array.Empty<PostBuilderSummaryResponse>();
        }

        // Collect first resource id per builder to batch-presign thumbnails.
        var firstResourceByBuilder = new Dictionary<Guid, Guid>(builders.Count);
        foreach (var builder in builders)
        {
            var ids = GeminiDraftPostHelper.ParseResourceIds(builder.ResourceIds);
            if (ids.Count > 0)
            {
                firstResourceByBuilder[builder.Id] = ids[0];
            }
        }

        var allFirstResourceIds = firstResourceByBuilder.Values.Distinct().ToList();
        var presignedById = new Dictionary<Guid, string>();
        if (allFirstResourceIds.Count > 0)
        {
            var presignResult = await userResourceService.GetPresignedResourcesAsync(
                userId,
                allFirstResourceIds,
                cancellationToken);

            if (presignResult.IsSuccess)
            {
                presignedById = presignResult.Value.ToDictionary(
                    item => item.ResourceId,
                    item => item.PresignedUrl);
            }
        }

        // Load all publications for live posts across every builder in one batch.
        var allPostIds = builders
            .SelectMany(builder => builder.Posts)
            .Where(post => post.DeletedAt is null)
            .Select(post => post.Id)
            .Distinct()
            .ToList();

        var publicationsByPostId = new Dictionary<Guid, List<PostPublication>>();
        if (allPostIds.Count > 0)
        {
            var publications = await postPublicationRepository.GetByPostIdsAsync(allPostIds, cancellationToken);
            publicationsByPostId = publications
                .GroupBy(publication => publication.PostId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        var summaries = new List<PostBuilderSummaryResponse>(builders.Count);
        foreach (var builder in builders)
        {
            var livePosts = builder.Posts.Where(post => post.DeletedAt is null).ToList();

            var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var publishedCount = 0;

            foreach (var post in livePosts)
            {
                var postPublications = publicationsByPostId.TryGetValue(post.Id, out var list)
                    ? list
                    : new List<PostPublication>();

                var isPostPublished = postPublications.Any(publication =>
                    string.Equals(publication.PublishStatus, "published", StringComparison.OrdinalIgnoreCase));

                if (isPostPublished)
                {
                    publishedCount++;
                }

                if (!string.IsNullOrWhiteSpace(post.Platform))
                {
                    platforms.Add(post.Platform!.Trim().ToLowerInvariant());
                }
                else
                {
                    foreach (var publication in postPublications)
                    {
                        if (!string.IsNullOrWhiteSpace(publication.SocialMediaType))
                        {
                            platforms.Add(publication.SocialMediaType!.Trim().ToLowerInvariant());
                        }
                    }
                }
            }

            var firstPostSnippet = livePosts
                .OrderBy(post => post.CreatedAt)
                .Select(post => post.Content?.Content)
                .FirstOrDefault(content => !string.IsNullOrWhiteSpace(content));

            string? thumbnailUrl = null;
            if (firstResourceByBuilder.TryGetValue(builder.Id, out var resourceId) &&
                presignedById.TryGetValue(resourceId, out var presigned))
            {
                thumbnailUrl = presigned;
            }

            summaries.Add(new PostBuilderSummaryResponse(
                Id: builder.Id,
                WorkspaceId: builder.WorkspaceId,
                OriginKind: builder.OriginKind,
                Type: builder.PostType,
                PostCount: livePosts.Count,
                PublishedCount: publishedCount,
                Platforms: platforms.ToList(),
                ThumbnailUrl: thumbnailUrl,
                FirstPostSnippet: Truncate(firstPostSnippet, 160),
                CreatedAt: builder.CreatedAt,
                UpdatedAt: builder.UpdatedAt));
        }

        return summaries;
    }

    private static string? Truncate(string? source, int max)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        return source.Length <= max ? source : source[..max] + "…";
    }
}
