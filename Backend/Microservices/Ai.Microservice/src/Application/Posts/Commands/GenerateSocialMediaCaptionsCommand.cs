using Application.Abstractions.Resources;
using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record GenerateSocialMediaCaptionsCommand(
    Guid UserId,
    IReadOnlyList<SocialMediaCaptionPostInput> SocialMedias,
    string? Language,
    string? Instruction) : IRequest<Result<GenerateSocialMediaCaptionsResponse>>;

public sealed record SocialMediaCaptionPostInput(
    Guid PostId,
    string SocialMediaType,
    IReadOnlyList<Guid> ResourceIds);

public sealed class GenerateSocialMediaCaptionsCommandHandler
    : IRequestHandler<GenerateSocialMediaCaptionsCommand, Result<GenerateSocialMediaCaptionsResponse>>
{
    private const int DefaultCaptionCount = 3;
    private const int MaxCaptionCount = 6;

    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserConfigService _userConfigService;
    private readonly IGeminiCaptionService _geminiCaptionService;

    public GenerateSocialMediaCaptionsCommandHandler(
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IUserConfigService userConfigService,
        IGeminiCaptionService geminiCaptionService)
    {
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _userConfigService = userConfigService;
        _geminiCaptionService = geminiCaptionService;
    }

    public async Task<Result<GenerateSocialMediaCaptionsResponse>> Handle(
        GenerateSocialMediaCaptionsCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedPlatformsResult = NormalizePlatforms(request.SocialMedias);
        if (normalizedPlatformsResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(normalizedPlatformsResult.Error);
        }

        var postsById = new Dictionary<Guid, Post>(normalizedPlatformsResult.Value.Count);
        foreach (var socialMedia in normalizedPlatformsResult.Value)
        {
            var post = await _postRepository.GetByIdAsync(socialMedia.PostId, cancellationToken);
            if (post is null || post.DeletedAt.HasValue)
            {
                return Result.Failure<GenerateSocialMediaCaptionsResponse>(PostErrors.NotFound);
            }

            if (post.UserId != request.UserId)
            {
                return Result.Failure<GenerateSocialMediaCaptionsResponse>(PostErrors.Unauthorized);
            }

            postsById[socialMedia.PostId] = post;
        }

        var allResourceIds = normalizedPlatformsResult.Value
            .SelectMany(item => item.ResourceIds)
            .Distinct()
            .ToList();

        var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            allResourceIds,
            cancellationToken);

        if (resourcesResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(resourcesResult.Error);
        }

        var resourcesById = resourcesResult.Value.ToDictionary(resource => resource.ResourceId);
        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var preferredModel = string.IsNullOrWhiteSpace(activeConfig?.ChatModel)
            ? null
            : activeConfig.ChatModel.Trim();
        var captionCount = Math.Clamp(
            activeConfig?.NumberOfVariances ?? DefaultCaptionCount,
            1,
            MaxCaptionCount);
        var languageHint = ResolveLanguageHint(request.Language);

        var responses = new List<SocialMediaCaptionsByPostResponse>(normalizedPlatformsResult.Value.Count);

        foreach (var socialMedia in normalizedPlatformsResult.Value)
        {
            if (!TryResolveResources(resourcesById, socialMedia.ResourceIds, out var orderedResources))
            {
                return Result.Failure<GenerateSocialMediaCaptionsResponse>(
                    new Error("Resource.NotFound", "One or more resources were not found."));
            }

            var geminiResources = orderedResources
                .Select(resource => new GeminiCaptionResource(
                    resource.PresignedUrl,
                    string.IsNullOrWhiteSpace(resource.ContentType)
                        ? "application/octet-stream"
                        : resource.ContentType.Trim()))
                .ToList();

            var geminiResult = await _geminiCaptionService.GenerateSocialMediaCaptionsAsync(
                new GeminiSocialMediaCaptionRequest(
                    geminiResources,
                    null,
                    socialMedia.SocialMediaType,
                    BuildResourceHints(postsById[socialMedia.PostId]),
                    captionCount,
                    languageHint,
                    request.Instruction,
                    preferredModel),
                cancellationToken);

            if (geminiResult.IsFailure)
            {
                return Result.Failure<GenerateSocialMediaCaptionsResponse>(geminiResult.Error);
            }

            responses.Add(new SocialMediaCaptionsByPostResponse(
                socialMedia.PostId,
                socialMedia.SocialMediaType,
                socialMedia.ResourceIds,
                geminiResult.Value
                    .Select(caption => new GeneratedCaptionResponse(
                        caption.Caption,
                        caption.Hashtags,
                        caption.TrendingHashtags,
                        caption.CallToAction))
                    .ToList()));
        }

        return Result.Success(new GenerateSocialMediaCaptionsResponse(responses));
    }

    private static Result<IReadOnlyList<SocialMediaCaptionPostInput>> NormalizePlatforms(
        IReadOnlyList<SocialMediaCaptionPostInput>? socialMedias)
    {
        if (socialMedias is null || socialMedias.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaCaptionPostInput>>(
                new Error("SocialMedia.InvalidRequest", "At least one social media item is required."));
        }

        var normalized = new List<SocialMediaCaptionPostInput>(socialMedias.Count);

        foreach (var socialMedia in socialMedias)
        {
            if (socialMedia.PostId == Guid.Empty)
            {
                return Result.Failure<IReadOnlyList<SocialMediaCaptionPostInput>>(
                    new Error("Post.InvalidRequest", "Each social media item must include a valid postId."));
            }

            var normalizedTypeResult = NormalizePlatformType(socialMedia.SocialMediaType);
            if (normalizedTypeResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SocialMediaCaptionPostInput>>(normalizedTypeResult.Error);
            }

            var resourceIds = socialMedia.ResourceIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (resourceIds.Count == 0)
            {
                return Result.Failure<IReadOnlyList<SocialMediaCaptionPostInput>>(
                    new Error("Resource.Missing", "Each social media item must include at least one resource."));
            }

            normalized.Add(new SocialMediaCaptionPostInput(
                socialMedia.PostId,
                normalizedTypeResult.Value,
                resourceIds));
        }

        return Result.Success<IReadOnlyList<SocialMediaCaptionPostInput>>(normalized);
    }

    private static Result<string> NormalizePlatformType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return Result.Failure<string>(
                new Error("SocialMedia.InvalidType", "Each social media item must include a type."));
        }

        var normalized = rawType.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "facebook" or "fb" => Result.Success("facebook"),
            "tiktok" => Result.Success("tiktok"),
            "instagram" or "ig" => Result.Success("ig"),
            "threads" => Result.Success("threads"),
            _ => Result.Failure<string>(
                new Error("SocialMedia.UnsupportedPlatform", "Only Facebook, Instagram, TikTok, and Threads are supported."))
        };
    }

    private static string? ResolveLanguageHint(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "vi" or "vn" or "vietnamese" => "Vietnamese",
            "en" or "english" => "English",
            _ => language.Trim()
        };
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
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

    private static IReadOnlyList<string> BuildResourceHints(Post post)
    {
        var hints = new List<string>();

        if (!string.IsNullOrWhiteSpace(post.Title))
        {
            hints.Add(post.Title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(post.Content?.Content))
        {
            var content = post.Content.Content.Trim();
            if (content.Length > 140)
            {
                content = content[..140].TrimEnd();
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                hints.Add(content);
            }
        }

        return hints;
    }
}
