using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
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
    IReadOnlyList<PrepareGeminiPostSocialMediaInput> SocialMedia,
    string? Language,
    string? Instruction) : IRequest<Result<PrepareGeminiPostsResponse>>;

public sealed record PrepareGeminiPostSocialMediaInput(
    string? Platform,
    string? Type,
    IReadOnlyList<Guid> ResourceIds);

public sealed class PrepareGeminiPostsCommandHandler
    : IRequestHandler<PrepareGeminiPostsCommand, Result<PrepareGeminiPostsResponse>>
{
    private const int DefaultDraftCount = 3;
    private const int MaxDraftCount = 6;

    private readonly IPostBuilderRepository _postBuilderRepository;
    private readonly IPostRepository _postRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly IGeminiCaptionService _geminiCaptionService;

    public PrepareGeminiPostsCommandHandler(
        IPostBuilderRepository postBuilderRepository,
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        IUserConfigService userConfigService,
        IUserResourceService userResourceService,
        IGeminiCaptionService geminiCaptionService)
    {
        _postBuilderRepository = postBuilderRepository;
        _postRepository = postRepository;
        _workspaceRepository = workspaceRepository;
        _userConfigService = userConfigService;
        _userResourceService = userResourceService;
        _geminiCaptionService = geminiCaptionService;
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

        var allResourceIds = normalizedSocialMedia
            .SelectMany(item => item.ResourceIds)
            .Distinct()
            .ToList();

        if (allResourceIds.Count == 0)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(
                new Error("Resource.Missing", "At least one resource is required."));
        }

        var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            allResourceIds,
            cancellationToken);

        if (resourcesResult.IsFailure)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(resourcesResult.Error);
        }

        var resourcesById = resourcesResult.Value.ToDictionary(resource => resource.ResourceId);
        var languageHint = GeminiDraftPostHelper.ResolveLanguageHint(request.Language);
        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var preferredModel = string.IsNullOrWhiteSpace(activeConfig?.ChatModel)
            ? null
            : activeConfig.ChatModel.Trim();
        var draftCount = Math.Clamp(
            activeConfig?.NumberOfVariances ?? DefaultDraftCount,
            1,
            MaxDraftCount);

        var captionTasks = normalizedSocialMedia
            .Select(async socialMedia =>
            {
                if (!TryResolveResources(resourcesById, socialMedia.ResourceIds, out var orderedResources))
                {
                    return Result.Failure<CaptionBatch>(
                        new Error("Resource.NotFound", "One or more resources were not found."));
                }

                var geminiResources = orderedResources
                    .Select(resource => new GeminiCaptionResource(
                        resource.PresignedUrl,
                        string.IsNullOrWhiteSpace(resource.ContentType)
                            ? "application/octet-stream"
                            : resource.ContentType))
                    .ToList();

                var captionsResult = await _geminiCaptionService.GenerateSocialMediaCaptionsAsync(
                    new GeminiSocialMediaCaptionRequest(
                        geminiResources,
                        null,
                        socialMedia.Platform,
                        Array.Empty<string>(),
                        draftCount,
                        languageHint,
                        request.Instruction,
                        preferredModel),
                    cancellationToken);

                if (captionsResult.IsFailure)
                {
                    return Result.Failure<CaptionBatch>(captionsResult.Error);
                }

                return Result.Success(new CaptionBatch(
                    null,
                    socialMedia.Platform,
                    socialMedia.PostType,
                    socialMedia.ResourceIds,
                    captionsResult.Value));
            })
            .ToList();

        var captionResults = await Task.WhenAll(captionTasks);
        var failedCaptionResult = captionResults.FirstOrDefault(result => result.IsFailure);
        if (failedCaptionResult is not null && failedCaptionResult.IsFailure)
        {
            return Result.Failure<PrepareGeminiPostsResponse>(failedCaptionResult.Error);
        }

        var responseGroups = new List<PreparedSocialMediaDraftGroupResponse>(captionResults.Length);
        var postBuilder = new PostBuilder
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            PostType = resolvedBuilderPostType,
            ResourceIds = GeminiDraftPostHelper.SerializeResourceIds(builderResourceIds),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postBuilderRepository.AddAsync(postBuilder, cancellationToken);

        foreach (var captionResult in captionResults)
        {
            var batch = captionResult.Value;
            var drafts = new List<PreparedDraftPostResponse>(batch.Captions.Count);

            foreach (var caption in batch.Captions)
            {
                var hashtags = caption.Hashtags.Count == 0
                    ? GeminiDraftPostHelper.ExtractHashtags(caption.Caption)
                    : caption.Hashtags;
                var title = GeminiDraftPostHelper.BuildDraftTitle(caption.Caption);

                var post = new Post
                {
                    Id = Guid.CreateVersion7(),
                    PostBuilderId = postBuilder.Id,
                    UserId = request.UserId,
                    WorkspaceId = workspaceId,
                    SocialMediaId = batch.SocialMediaId,
                    Platform = batch.Platform,
                    Title = string.IsNullOrWhiteSpace(title) ? null : title,
                    Content = new PostContent
                    {
                        Content = caption.Caption,
                        Hashtag = hashtags.Count == 0 ? null : string.Join(' ', hashtags),
                        ResourceList = batch.ResourceIds.Select(id => id.ToString()).ToList(),
                        PostType = batch.PostType
                    },
                    Status = "draft",
                    CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
                };

                await _postRepository.AddAsync(post, cancellationToken);

                drafts.Add(new PreparedDraftPostResponse(
                    post.Id,
                    post.Status ?? "draft",
                    batch.PostType,
                    caption.Caption,
                    title,
                    batch.ResourceIds,
                    hashtags,
                    caption.TrendingHashtags,
                    caption.CallToAction));
            }

            responseGroups.Add(new PreparedSocialMediaDraftGroupResponse(
                batch.SocialMediaId,
                batch.Platform,
                batch.PostType,
                batch.ResourceIds,
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

            if (resourceIds.Count == 0 && builderResourceIds.Count > 0)
            {
                resourceIds = builderResourceIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
            }

            if (resourceIds.Count == 0)
            {
                return Result.Failure<IReadOnlyList<ResolvedSocialMediaInput>>(
                    new Error("Resource.Missing", "Each social media item must include at least one resource or use builder resourceIds."));
            }

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

    private static bool TryResolveResources(
        IReadOnlyDictionary<Guid, UserResourcePresignResult> resourcesById,
        IReadOnlyList<Guid> resourceIds,
        out List<UserResourcePresignResult> orderedResources)
    {
        orderedResources = new List<UserResourcePresignResult>(resourceIds.Count);
        foreach (var resourceId in resourceIds)
        {
            if (!resourcesById.TryGetValue(resourceId, out var resource))
            {
                return false;
            }

            orderedResources.Add(resource);
        }

        return true;
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private sealed record ResolvedSocialMediaInput(
        string Platform,
        string PostType,
        IReadOnlyList<Guid> ResourceIds);

    private sealed record CaptionBatch(
        Guid? SocialMediaId,
        string Platform,
        string PostType,
        IReadOnlyList<Guid> ResourceIds,
        IReadOnlyList<GeminiGeneratedCaption> Captions);
}
