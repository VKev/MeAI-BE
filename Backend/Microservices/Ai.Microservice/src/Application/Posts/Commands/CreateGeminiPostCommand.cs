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

public sealed record CreateGeminiPostCommand(
    Guid UserId,
    Guid? WorkspaceId,
    IReadOnlyList<Guid> ResourceIds,
    string? Caption,
    string? PostType,
    string? Language,
    string? Instruction) : IRequest<Result<FacebookDraftPostResponse>>;

public sealed class CreateGeminiPostCommandHandler
    : IRequestHandler<CreateGeminiPostCommand, Result<FacebookDraftPostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IPostBuilderRepository _postBuilderRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly IGeminiCaptionService _geminiCaptionService;

    public CreateGeminiPostCommandHandler(
        IPostRepository postRepository,
        IPostBuilderRepository postBuilderRepository,
        IWorkspaceRepository workspaceRepository,
        IUserConfigService userConfigService,
        IUserResourceService userResourceService,
        IGeminiCaptionService geminiCaptionService)
    {
        _postRepository = postRepository;
        _postBuilderRepository = postBuilderRepository;
        _workspaceRepository = workspaceRepository;
        _userConfigService = userConfigService;
        _userResourceService = userResourceService;
        _geminiCaptionService = geminiCaptionService;
    }

    public async Task<Result<FacebookDraftPostResponse>> Handle(
        CreateGeminiPostCommand request,
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
                return Result.Failure<FacebookDraftPostResponse>(PostErrors.WorkspaceNotFound);
            }
        }

        var resolvedPostType = GeminiDraftPostHelper.NormalizePostType(request.PostType);
        if (!GeminiDraftPostHelper.IsSupportedPostType(resolvedPostType))
        {
            return Result.Failure<FacebookDraftPostResponse>(
                new Error("Facebook.InvalidPostType", "Post type must be 'posts' or 'reels'."));
        }

        var resourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (resourceIds.Count == 0)
        {
            return Result.Failure<FacebookDraftPostResponse>(
                new Error("Resource.Missing", "At least one resource is required."));
        }

        var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            resourceIds,
            cancellationToken);

        if (resourcesResult.IsFailure)
        {
            return Result.Failure<FacebookDraftPostResponse>(resourcesResult.Error);
        }

        var resources = resourcesResult.Value.ToList();

        if (string.Equals(resolvedPostType, "reels", StringComparison.OrdinalIgnoreCase))
        {
            if (resources.Any(IsImageResource))
            {
                return Result.Failure<FacebookDraftPostResponse>(
                    new Error("Facebook.InvalidResource", "Reels do not support image resources."));
            }

            if (resources.Any(resource => !IsVideoResource(resource)))
            {
                return Result.Failure<FacebookDraftPostResponse>(
                    new Error("Facebook.InvalidResource", "Reels require video resources."));
            }
        }

        var caption = request.Caption?.Trim();
        var captionGenerated = false;

        var languageHint = GeminiDraftPostHelper.ResolveLanguageHint(request.Language);
        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var preferredModel = string.IsNullOrWhiteSpace(activeConfig?.ChatModel)
            ? null
            : activeConfig.ChatModel.Trim();

        if (string.IsNullOrWhiteSpace(caption))
        {
            var geminiResources = resources.Select(resource => new GeminiCaptionResource(
                resource.PresignedUrl,
                string.IsNullOrWhiteSpace(resource.ContentType) ? "application/octet-stream" : resource.ContentType))
                .ToList();

            var geminiResult = await _geminiCaptionService.GenerateCaptionAsync(
                new GeminiCaptionRequest(geminiResources, resolvedPostType, languageHint, request.Instruction, preferredModel),
                cancellationToken);

            if (geminiResult.IsFailure)
            {
                return Result.Failure<FacebookDraftPostResponse>(geminiResult.Error);
            }

            caption = geminiResult.Value.Trim();
            captionGenerated = true;
        }

        caption ??= string.Empty;
        var titleSource = GeminiDraftPostHelper.NormalizeTitleContent(caption);
        var titleResult = await _geminiCaptionService.GenerateTitleAsync(
            new GeminiTitleRequest(titleSource, languageHint, preferredModel),
            cancellationToken);

        if (titleResult.IsFailure)
        {
            return Result.Failure<FacebookDraftPostResponse>(titleResult.Error);
        }

        var hashtags = GeminiDraftPostHelper.ExtractHashtags(caption);
        var hashtagValue = hashtags.Count == 0 ? null : string.Join(' ', hashtags);
        var title = titleResult.Value.Trim();

        var postContent = new PostContent
        {
            Content = caption,
            Hashtag = hashtagValue,
            ResourceList = resourceIds.Select(id => id.ToString()).ToList(),
            PostType = resolvedPostType
        };

        var postBuilder = new PostBuilder
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            OriginKind = PostBuilderOriginKinds.AiGeminiDraft,
            PostType = resolvedPostType,
            ResourceIds = GeminiDraftPostHelper.SerializeResourceIds(resourceIds),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
            UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        var post = new Post
        {
            Id = Guid.CreateVersion7(),
            PostBuilderId = postBuilder.Id,
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Content = postContent,
            Status = "draft",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postBuilderRepository.AddAsync(postBuilder, cancellationToken);
        await _postRepository.AddAsync(post, cancellationToken);
        await _postRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new FacebookDraftPostResponse(
            post.Id,
            postBuilder.Id,
            post.Status ?? "draft",
            resolvedPostType,
            caption ?? string.Empty,
            resourceIds,
            captionGenerated));
    }
    private static bool IsImageResource(UserResourcePresignResult resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            resource.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(resource.ResourceType) &&
               resource.ResourceType.Contains("image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoResource(UserResourcePresignResult resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            resource.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(resource.ResourceType) &&
               resource.ResourceType.Contains("video", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
