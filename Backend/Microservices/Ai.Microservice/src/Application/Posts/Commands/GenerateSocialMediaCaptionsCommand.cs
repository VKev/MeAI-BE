using Application.Abstractions.Configs;
using Application.Abstractions.Gemini;
using Application.Posts.Models;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record GenerateSocialMediaCaptionsCommand(
    Guid UserId,
    GeminiTemplateResourceInput TemplateResource,
    IReadOnlyList<SocialMediaCaptionPlatformInput> SocialMedias,
    string? Language,
    string? Instruction) : IRequest<Result<GenerateSocialMediaCaptionsResponse>>;

public sealed record GeminiTemplateResourceInput(
    string FileName,
    string MimeType,
    byte[] Content);

public sealed record SocialMediaCaptionPlatformInput(
    string Type,
    IReadOnlyList<string> ResourceList);

public sealed class GenerateSocialMediaCaptionsCommandHandler
    : IRequestHandler<GenerateSocialMediaCaptionsCommand, Result<GenerateSocialMediaCaptionsResponse>>
{
    private const int DefaultCaptionCount = 3;
    private const int MaxCaptionCount = 6;

    private readonly IUserConfigService _userConfigService;
    private readonly IGeminiCaptionService _geminiCaptionService;

    public GenerateSocialMediaCaptionsCommandHandler(
        IUserConfigService userConfigService,
        IGeminiCaptionService geminiCaptionService)
    {
        _userConfigService = userConfigService;
        _geminiCaptionService = geminiCaptionService;
    }

    public async Task<Result<GenerateSocialMediaCaptionsResponse>> Handle(
        GenerateSocialMediaCaptionsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.TemplateResource.Content.Length == 0)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(
                new Error("Gemini.TemplateResourceMissing", "Template resource is required."));
        }

        var normalizedPlatformsResult = NormalizePlatforms(request.SocialMedias);
        if (normalizedPlatformsResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(normalizedPlatformsResult.Error);
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var preferredModel = string.IsNullOrWhiteSpace(activeConfig?.ChatModel)
            ? null
            : activeConfig.ChatModel.Trim();
        var captionCount = Math.Clamp(
            activeConfig?.NumberOfVariances ?? DefaultCaptionCount,
            1,
            MaxCaptionCount);
        var languageHint = ResolveLanguageHint(request.Language);
        var templateMimeType = string.IsNullOrWhiteSpace(request.TemplateResource.MimeType)
            ? "application/octet-stream"
            : request.TemplateResource.MimeType.Trim();

        var responses = new List<SocialMediaCaptionsByPlatformResponse>(normalizedPlatformsResult.Value.Count);

        foreach (var socialMedia in normalizedPlatformsResult.Value)
        {
            var geminiResult = await _geminiCaptionService.GenerateSocialMediaCaptionsAsync(
                new GeminiSocialMediaCaptionRequest(
                    Array.Empty<GeminiCaptionResource>(),
                    new GeminiInlineCaptionResource(templateMimeType, request.TemplateResource.Content),
                    socialMedia.Type,
                    socialMedia.ResourceList,
                    captionCount,
                    languageHint,
                    request.Instruction,
                    preferredModel),
                cancellationToken);

            if (geminiResult.IsFailure)
            {
                return Result.Failure<GenerateSocialMediaCaptionsResponse>(geminiResult.Error);
            }

            responses.Add(new SocialMediaCaptionsByPlatformResponse(
                socialMedia.Type,
                socialMedia.ResourceList,
                geminiResult.Value
                    .Select(caption => new GeneratedCaptionResponse(
                        caption.Caption,
                        caption.Hashtags,
                        caption.TrendingHashtags,
                        caption.CallToAction))
                    .ToList()));
        }

        return Result.Success(new GenerateSocialMediaCaptionsResponse(
            string.IsNullOrWhiteSpace(request.TemplateResource.FileName)
                ? "template-resource"
                : request.TemplateResource.FileName.Trim(),
            templateMimeType,
            responses));
    }

    private static Result<IReadOnlyList<SocialMediaCaptionPlatformInput>> NormalizePlatforms(
        IReadOnlyList<SocialMediaCaptionPlatformInput>? socialMedias)
    {
        if (socialMedias is null || socialMedias.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaCaptionPlatformInput>>(
                new Error("SocialMedia.InvalidRequest", "At least one social media item is required."));
        }

        var mergedPlatforms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var socialMedia in socialMedias)
        {
            var normalizedTypeResult = NormalizePlatformType(socialMedia.Type);
            if (normalizedTypeResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SocialMediaCaptionPlatformInput>>(normalizedTypeResult.Error);
            }

            if (!mergedPlatforms.TryGetValue(normalizedTypeResult.Value, out var resources))
            {
                resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                mergedPlatforms[normalizedTypeResult.Value] = resources;
            }

            foreach (var resource in socialMedia.ResourceList ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(resource))
                {
                    continue;
                }

                resources.Add(resource.Trim());
            }
        }

        if (mergedPlatforms.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaCaptionPlatformInput>>(
                new Error("SocialMedia.InvalidRequest", "At least one social media item is required."));
        }

        var normalized = mergedPlatforms
            .Select(item => new SocialMediaCaptionPlatformInput(
                item.Key,
                item.Value.ToList()))
            .ToList();

        return Result.Success<IReadOnlyList<SocialMediaCaptionPlatformInput>>(normalized);
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
}
