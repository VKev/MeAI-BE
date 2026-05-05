using Application.Abstractions.Resources;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record PrepareGeminiPostsCommand(
    Guid UserId,
    Guid? WorkspaceId,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<PrepareGeminiPostSocialMediaInput> SocialMedia) : IRequest<Result<PrepareGeminiPostsResponse>>;

public sealed record PrepareGeminiPostSocialMediaInput(
    string? Platform,
    string? Type,
    IReadOnlyList<Guid> ResourceIds);

public sealed class PrepareGeminiPostsCommandHandler
    : IRequestHandler<PrepareGeminiPostsCommand, Result<PrepareGeminiPostsResponse>>
{
    private readonly IPostBuilderRepository _postBuilderRepository;
    private readonly IPostRepository _postRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUserResourceService _userResourceService;

    public PrepareGeminiPostsCommandHandler(
        IPostBuilderRepository postBuilderRepository,
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        IUserResourceService userResourceService)
    {
        _postBuilderRepository = postBuilderRepository;
        _postRepository = postRepository;
        _workspaceRepository = workspaceRepository;
        _userResourceService = userResourceService;
    }

    public async Task<Result<PrepareGeminiPostsResponse>> Handle(
        PrepareGeminiPostsCommand request,
        CancellationToken cancellationToken)
    {
        var workspaceId = request.WorkspaceId == Guid.Empty ? null : request.WorkspaceId;
        if (workspaceId.HasValue)
        {
            var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
                workspaceId.Value,
                request.UserId,
                cancellationToken);

            if (!workspaceExists)
            {
                return Result.Failure<PrepareGeminiPostsResponse>(PostErrors.WorkspaceNotFound);
            }
        }

        if (request.SocialMedia is null || request.SocialMedia.Count == 0)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(
                new Error("SocialMedia.InvalidRequest", "At least one social media item is required."));
        }

        var normalizedSocialMediaResult = NormalizeSocialMediaInputs(
            request.ResourceIds,
            request.SocialMedia);

        if (normalizedSocialMediaResult.IsFailure)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(normalizedSocialMediaResult.Error);
        }

        var normalizedSocialMedia = normalizedSocialMediaResult.Value;
        var resolvedBuilderPostType = ResolveBuilderPostType(normalizedSocialMedia);

        var builderResourceIds = request.ResourceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (builderResourceIds.Count == 0)
        {
            builderResourceIds = normalizedSocialMedia
                .SelectMany(item => item.ResourceIds)
                .Distinct()
                .ToList();
        }

        // Resources can live ONLY at the builder level (per-platform Posts then
        // store an empty resource list — the caller is opting in to "builder
        // owns the bundle, no platform-specific bind"), or the caller can
        // explicitly attach per-platform resources. Either way, validate that
        // every referenced resource exists.
        var allResourceIds = normalizedSocialMedia
            .SelectMany(item => item.ResourceIds)
            .Concat(builderResourceIds)
            .Distinct()
            .ToList();

        if (allResourceIds.Count == 0)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(
                new Error("Resource.Missing", "At least one resource is required (builder-level or any per-platform)."));
        }

        var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            allResourceIds,
            cancellationToken);

        if (resourcesResult.IsFailure)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(resourcesResult.Error);
        }

        var returnedResourceIds = resourcesResult.Value
            .Select(resource => resource.ResourceId)
            .Distinct()
            .ToHashSet();

        var missingResourceId = allResourceIds.FirstOrDefault(id => !returnedResourceIds.Contains(id));
        if (missingResourceId != Guid.Empty)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(
                new Error("Resource.NotFound", "One or more resources were not found."));
        }

        var responseGroups = new List<PreparedSocialMediaDraftGroupResponse>(normalizedSocialMedia.Count);
        var postBuilder = new PostBuilder
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            OriginKind = PostBuilderOriginKinds.AiGeminiDraft,
            PostType = resolvedBuilderPostType,
            ResourceIds = GeminiDraftPostHelper.SerializeResourceIds(builderResourceIds),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postBuilderRepository.AddAsync(postBuilder, cancellationToken);

        foreach (var socialMedia in normalizedSocialMedia)
        {
            var post = new Post
            {
                Id = Guid.CreateVersion7(),
                PostBuilderId = postBuilder.Id,
                UserId = request.UserId,
                WorkspaceId = workspaceId,
                SocialMediaId = null,
                Platform = socialMedia.Platform,
                Title = null,
                Content = new PostContent
                {
                    Content = null,
                    Hashtag = null,
                    ResourceList = socialMedia.ResourceIds.Select(id => id.ToString()).ToList(),
                    PostType = socialMedia.PostType
                },
                Status = "draft",
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };

            await _postRepository.AddAsync(post, cancellationToken);

            var drafts = new List<PreparedDraftPostResponse>(1)
            {
                new(
                    post.Id,
                    post.Status ?? "draft",
                    socialMedia.PostType,
                    string.Empty,
                    null,
                    socialMedia.ResourceIds,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    null)
            };

            responseGroups.Add(new PreparedSocialMediaDraftGroupResponse(
                null,
                socialMedia.Platform,
                socialMedia.PostType,
                socialMedia.ResourceIds,
                drafts));
        }

        await _postRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new PrepareGeminiPostsResponse(
            postBuilder.Id,
            workspaceId,
            resolvedBuilderPostType,
            responseGroups,
            builderResourceIds));
    }

    private static Result<IReadOnlyList<ResolvedSocialMediaInput>> NormalizeSocialMediaInputs(
        IReadOnlyList<Guid> builderResourceIds,
        IReadOnlyList<PrepareGeminiPostSocialMediaInput> socialMediaInputs)
    {
        var normalized = new List<ResolvedSocialMediaInput>(socialMediaInputs.Count);

        foreach (var item in socialMediaInputs)
        {
            var resourceIds = item.ResourceIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            // Per-platform resourceIds are NEVER auto-filled from the builder list.
            // Empty stays empty — that means "this platform's Post has no platform-
            // specific resources; the bundle lives on PostBuilder only". Whatever
            // the caller specified is exactly what gets persisted on Post.Content.

            if (builderResourceIds.Count > 0 && resourceIds.Any(id => !builderResourceIds.Contains(id)))
            {
                return Result.Failure<IReadOnlyList<ResolvedSocialMediaInput>>(
                    new Error("Resource.InvalidRequest", "socialMedia.resourceIds must be contained in the builder resourceIds list."));
            }

            var platformResult = GeminiDraftPostHelper.NormalizePlatformType(item.Platform);
            if (platformResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ResolvedSocialMediaInput>>(platformResult.Error);
            }

            var postType = GeminiDraftPostHelper.NormalizePostType(item.Type);
            if (!GeminiDraftPostHelper.IsSupportedPostType(postType))
            {
                return Result.Failure<IReadOnlyList<ResolvedSocialMediaInput>>(
                    new Error("Post.InvalidPostType", "Post type must be 'posts' or 'reels'."));
            }

            normalized.Add(new ResolvedSocialMediaInput(
                platformResult.Value,
                postType,
                resourceIds));
        }

        return Result.Success<IReadOnlyList<ResolvedSocialMediaInput>>(normalized);
    }

    private static string? ResolveBuilderPostType(IReadOnlyList<ResolvedSocialMediaInput> socialMedia)
    {
        var distinctTypes = socialMedia
            .Select(item => item.PostType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinctTypes.Count == 1 ? distinctTypes[0] : null;
    }

    private sealed record ResolvedSocialMediaInput(
        string Platform,
        string PostType,
        IReadOnlyList<Guid> ResourceIds);
}
