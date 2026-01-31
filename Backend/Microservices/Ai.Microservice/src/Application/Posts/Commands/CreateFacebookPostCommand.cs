using System.Text.RegularExpressions;
using Application.Abstractions.Gemini;
using Application.Abstractions.Resources;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record CreateFacebookPostCommand(
    Guid UserId,
    IReadOnlyList<Guid> ResourceIds,
    string? Caption,
    string? PostType,
    string? Language) : IRequest<Result<FacebookDraftPostResponse>>;

public sealed class CreateFacebookPostCommandHandler
    : IRequestHandler<CreateFacebookPostCommand, Result<FacebookDraftPostResponse>>
{
    private const string DefaultPostType = "posts";

    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IGeminiCaptionService _geminiCaptionService;
    private static readonly Regex HashtagRegex = new(@"#([\p{L}\p{Mn}\p{Nd}_]+)", RegexOptions.Compiled);
    private static readonly Regex CollapseWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    public CreateFacebookPostCommandHandler(
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IGeminiCaptionService geminiCaptionService)
    {
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _geminiCaptionService = geminiCaptionService;
    }

    public async Task<Result<FacebookDraftPostResponse>> Handle(
        CreateFacebookPostCommand request,
        CancellationToken cancellationToken)
    {
        var resolvedPostType = NormalizePostType(request.PostType);
        if (!IsSupportedPostType(resolvedPostType))
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

        var languageHint = ResolveLanguageHint(request.Language);

        if (string.IsNullOrWhiteSpace(caption))
        {
            var geminiResources = resources.Select(resource => new GeminiCaptionResource(
                resource.PresignedUrl,
                string.IsNullOrWhiteSpace(resource.ContentType) ? "application/octet-stream" : resource.ContentType))
                .ToList();

            var geminiResult = await _geminiCaptionService.GenerateCaptionAsync(
                new GeminiCaptionRequest(geminiResources, resolvedPostType, languageHint),
                cancellationToken);

            if (geminiResult.IsFailure)
            {
                return Result.Failure<FacebookDraftPostResponse>(geminiResult.Error);
            }

            caption = geminiResult.Value.Trim();
            captionGenerated = true;
        }

        caption ??= string.Empty;
        var titleSource = NormalizeTitleContent(caption);
        var titleResult = await _geminiCaptionService.GenerateTitleAsync(
            new GeminiTitleRequest(titleSource, languageHint),
            cancellationToken);

        if (titleResult.IsFailure)
        {
            return Result.Failure<FacebookDraftPostResponse>(titleResult.Error);
        }

        var hashtags = ExtractHashtags(caption);
        var hashtagValue = hashtags.Count == 0 ? null : string.Join(' ', hashtags);
        var title = titleResult.Value.Trim();

        var postContent = new PostContent
        {
            Content = caption,
            Hashtag = hashtagValue,
            ResourceList = resourceIds.Select(id => id.ToString()).ToList(),
            PostType = resolvedPostType
        };

        var post = new Post
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Content = postContent,
            Status = "draft",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postRepository.AddAsync(post, cancellationToken);
        await _postRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new FacebookDraftPostResponse(
            post.Id,
            post.Status ?? "draft",
            resolvedPostType,
            caption ?? string.Empty,
            resourceIds,
            captionGenerated));
    }

    private static string NormalizePostType(string? postType)
    {
        if (string.IsNullOrWhiteSpace(postType))
        {
            return DefaultPostType;
        }

        var normalized = postType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "post" => "posts",
            "posts" => "posts",
            "reel" => "reels",
            "reels" => "reels",
            _ => postType.Trim()
        };
    }

    private static bool IsSupportedPostType(string? postType) =>
        string.Equals(postType, "posts", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(postType, "reels", StringComparison.OrdinalIgnoreCase);

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

    private static string? ResolveLanguageHint(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "vi" => "Vietnamese",
            "vn" => "Vietnamese",
            "vietnamese" => "Vietnamese",
            "en" => "English",
            "english" => "English",
            _ => null
        };
    }

    private static IReadOnlyList<string> ExtractHashtags(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return Array.Empty<string>();
        }

        var matches = HashtagRegex.Matches(caption);
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashtags = new List<string>(matches.Count);

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var value = match.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (unique.Add(value))
            {
                hashtags.Add(value);
            }
        }

        return hashtags;
    }

    private static string NormalizeTitleContent(string caption)
    {
        var withoutHashtags = HashtagRegex.Replace(caption, string.Empty);
        var collapsed = CollapseWhitespaceRegex.Replace(withoutHashtags, " ");
        return string.IsNullOrWhiteSpace(collapsed) ? caption : collapsed.Trim();
    }
}
