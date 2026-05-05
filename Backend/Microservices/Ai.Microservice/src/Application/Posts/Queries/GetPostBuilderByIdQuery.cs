using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetPostBuilderByIdQuery(Guid PostBuilderId, Guid UserId)
    : IRequest<Result<PostBuilderDetailsResponse>>;

public sealed class GetPostBuilderByIdQueryHandler
    : IRequestHandler<GetPostBuilderByIdQuery, Result<PostBuilderDetailsResponse>>
{
    private readonly IPostBuilderRepository _postBuilderRepository;
    private readonly PostResponseBuilder _postResponseBuilder;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IUserResourceService _userResourceService;

    public GetPostBuilderByIdQueryHandler(
        IPostBuilderRepository postBuilderRepository,
        PostResponseBuilder postResponseBuilder,
        IUserSocialMediaService userSocialMediaService,
        IUserResourceService userResourceService)
    {
        _postBuilderRepository = postBuilderRepository;
        _postResponseBuilder = postResponseBuilder;
        _userSocialMediaService = userSocialMediaService;
        _userResourceService = userResourceService;
    }

    public async Task<Result<PostBuilderDetailsResponse>> Handle(
        GetPostBuilderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var postBuilder = await _postBuilderRepository.GetByIdAsync(request.PostBuilderId, cancellationToken);
        if (postBuilder is null || postBuilder.DeletedAt.HasValue)
        {
            return Result.Failure<PostBuilderDetailsResponse>(PostBuilderErrors.NotFound);
        }

        if (postBuilder.UserId != request.UserId)
        {
            return Result.Failure<PostBuilderDetailsResponse>(PostBuilderErrors.Unauthorized);
        }

        var posts = postBuilder.Posts
            .Where(post => post.DeletedAt is null)
            .OrderBy(post => post.CreatedAt)
            .ThenBy(post => post.Id)
            .ToList();

        var postResponses = await _postResponseBuilder.BuildManyAsync(request.UserId, posts, cancellationToken);
        var postResponseById = postResponses.ToDictionary(post => post.Id);

        var socialMediaIds = posts
            .Where(post => post.SocialMediaId.HasValue && post.SocialMediaId.Value != Guid.Empty)
            .Select(post => post.SocialMediaId!.Value)
            .Distinct()
            .ToList();

        var socialMediaTypesById = new Dictionary<Guid, string>(socialMediaIds.Count);
        if (socialMediaIds.Count > 0)
        {
            var socialMediasResult = await _userSocialMediaService.GetSocialMediasAsync(
                request.UserId,
                socialMediaIds,
                cancellationToken);

            if (socialMediasResult.IsFailure)
            {
                return Result.Failure<PostBuilderDetailsResponse>(socialMediasResult.Error);
            }

            socialMediaTypesById = socialMediasResult.Value
                .ToDictionary(item => item.SocialMediaId, item => item.Type);
        }

        var groupLookup = new Dictionary<string, PostBuilderSocialMediaGroupBuilder>(StringComparer.Ordinal);

        foreach (var post in posts)
        {
            var platform = ResolvePlatform(post, socialMediaTypesById);
            var type = ResolvePostType(post, postBuilder);
            var groupKey = $"{post.SocialMediaId?.ToString() ?? "none"}:{platform}:{type}";

            if (!groupLookup.TryGetValue(groupKey, out var group))
            {
                group = new PostBuilderSocialMediaGroupBuilder(
                    post.SocialMediaId,
                    platform,
                    type);
                groupLookup[groupKey] = group;
            }

            if (postResponseById.TryGetValue(post.Id, out var postResponse))
            {
                group.Posts.Add(postResponse);
            }
        }

        var finalizedGroups = groupLookup.Values
            .Select(group => new PostBuilderSocialMediaGroupResponse(
                group.SocialMediaId,
                group.Platform,
                group.Type,
                group.Posts))
            .ToList();

        // Hydrate builder-level resourceIds the same way the per-post `media[]`
        // is hydrated by PostResponseBuilder — same shape (resourceId,
        // presignedUrl, contentType, resourceType) — so callers can render
        // builder-level resources without a second round-trip.
        var builderResourceIds = GeminiDraftPostHelper.ParseResourceIds(postBuilder.ResourceIds);
        var builderResources = await BuildBuilderResourcesAsync(
            request.UserId, builderResourceIds, cancellationToken);

        return Result.Success(new PostBuilderDetailsResponse(
            postBuilder.Id,
            postBuilder.WorkspaceId,
            postBuilder.OriginKind,
            postBuilder.PostType,
            builderResources,
            finalizedGroups,
            postBuilder.CreatedAt,
            postBuilder.UpdatedAt));
    }

    private async Task<IReadOnlyList<PostMediaResponse>> BuildBuilderResourcesAsync(
        Guid userId,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.Count == 0)
        {
            return Array.Empty<PostMediaResponse>();
        }

        var presignResult = await _userResourceService.GetPresignedResourcesAsync(
            userId, resourceIds, cancellationToken);
        if (presignResult.IsFailure)
        {
            // Best-effort: the IDs are still surfaced via `resourceIds`, so a
            // hydrate failure shouldn't fail the whole detail call.
            return Array.Empty<PostMediaResponse>();
        }

        var resourcesById = presignResult.Value.ToDictionary(r => r.ResourceId, r => r);

        var hydrated = new List<PostMediaResponse>(resourceIds.Count);
        foreach (var id in resourceIds)
        {
            if (resourcesById.TryGetValue(id, out var resource))
            {
                hydrated.Add(new PostMediaResponse(
                    resource.ResourceId,
                    resource.PresignedUrl,
                    resource.ContentType,
                    resource.ResourceType));
            }
        }

        return hydrated;
    }

    private static string ResolvePlatform(Post post, IReadOnlyDictionary<Guid, string> socialMediaTypesById)
    {
        if (!string.IsNullOrWhiteSpace(post.Platform))
        {
            var platformResult = GeminiDraftPostHelper.NormalizePlatformType(post.Platform);
            if (platformResult.IsSuccess)
            {
                return platformResult.Value;
            }
        }

        if (post.SocialMediaId.HasValue &&
            socialMediaTypesById.TryGetValue(post.SocialMediaId.Value, out var socialMediaType))
        {
            var platformResult = GeminiDraftPostHelper.NormalizePlatformType(socialMediaType);
            if (platformResult.IsSuccess)
            {
                return platformResult.Value;
            }
        }

        return "unknown";
    }

    private static string ResolvePostType(Post post, PostBuilder postBuilder)
    {
        var resolved = GeminiDraftPostHelper.NormalizePostType(post.Content?.PostType ?? postBuilder.PostType);
        return GeminiDraftPostHelper.IsSupportedPostType(resolved)
            ? resolved
            : GeminiDraftPostHelper.NormalizePostType(null);
    }

    private sealed class PostBuilderSocialMediaGroupBuilder
    {
        public PostBuilderSocialMediaGroupBuilder(Guid? socialMediaId, string platform, string type)
        {
            SocialMediaId = socialMediaId;
            Platform = platform;
            Type = type;
        }

        public Guid? SocialMediaId { get; }

        public string Platform { get; }

        public string Type { get; }

        public List<PostResponse> Posts { get; } = [];
    }
}
