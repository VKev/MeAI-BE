using System.Text.RegularExpressions;
using Application.Abstractions.Agents;
using Application.Abstractions.Automation;
using Application.Agents;
using Application.Posts.Commands;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Agents;

public sealed partial class ChatWebPostService : IChatWebPostService
{
    private const int MaxUrlsPerPrompt = 3;
    private const int SearchResultCount = 5;

    private readonly IAgentWebSearchService _agentWebSearchService;
    private readonly IWebSearchEnrichmentService _webSearchEnrichmentService;
    private readonly IAgenticRuntimeContentService _runtimeContentService;
    private readonly IMediator _mediator;

    public ChatWebPostService(
        IAgentWebSearchService agentWebSearchService,
        IWebSearchEnrichmentService webSearchEnrichmentService,
        IAgenticRuntimeContentService runtimeContentService,
        IMediator mediator)
    {
        _agentWebSearchService = agentWebSearchService;
        _webSearchEnrichmentService = webSearchEnrichmentService;
        _runtimeContentService = runtimeContentService;
        _mediator = mediator;
    }

    public async Task<Result<ChatWebPostResult>> CreateDraftAsync(
        ChatWebPostRequest request,
        CancellationToken cancellationToken)
    {
        var urls = ExtractUrls(request.Prompt);
        Result<AgentWebSearchResponse> contentResult;
        var retrievalMode = urls.Count > 0 ? "direct_url" : "web_search";

        if (urls.Count > 0)
        {
            var enriched = await _webSearchEnrichmentService.EnrichUrlsAsync(
                urls,
                request.Prompt,
                request.UserId,
                request.WorkspaceId,
                request.SessionId,
                request.OriginChatId,
                cancellationToken);

            contentResult = Result.Success(enriched);
        }
        else
        {
            contentResult = await _agentWebSearchService.SearchAsync(
                new AgentWebSearchRequest(
                    Query: request.Prompt,
                    Count: SearchResultCount,
                    Freshness: "pd",
                    UserId: request.UserId,
                    WorkspaceId: request.WorkspaceId,
                    OriginChatSessionId: request.SessionId,
                    OriginChatId: request.OriginChatId),
                cancellationToken);
        }

        if (contentResult.IsFailure)
        {
            return Result.Failure<ChatWebPostResult>(contentResult.Error);
        }

        var content = contentResult.Value;
        var usableSources = content.Results
            .Where(result =>
                !string.IsNullOrWhiteSpace(result.Url) &&
                (!string.IsNullOrWhiteSpace(result.PageContent) ||
                 !string.IsNullOrWhiteSpace(result.Description) ||
                 result.MediaUrls is { Count: > 0 }))
            .ToList();

        if (usableSources.Count == 0 && (content.ImportedResources?.Count ?? 0) == 0)
        {
            return Result.Failure<ChatWebPostResult>(AgentErrors.WebContentNotFound);
        }

        var draftResult = await _runtimeContentService.GeneratePostDraftAsync(
            new AgenticRuntimeContentRequest(
                Guid.CreateVersion7(),
                request.SuggestedTitle,
                request.Prompt,
                null,
                null,
                content with { Results = usableSources.Count > 0 ? usableSources : content.Results },
                request.UserId,
                request.WorkspaceId,
                request.SessionId,
                request.OriginChatId),
            cancellationToken);

        if (draftResult.IsFailure)
        {
            return Result.Failure<ChatWebPostResult>(draftResult.Error);
        }

        var importedResourceIds = draftResult.Value.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        var postResult = await _mediator.Send(
            new CreatePostCommand(
                request.UserId,
                request.WorkspaceId,
                request.SessionId,
                null,
                draftResult.Value.Title ?? request.SuggestedTitle ?? BuildFallbackTitle(request.Prompt),
                new PostContent
                {
                    Content = draftResult.Value.Content,
                    Hashtag = draftResult.Value.Hashtag,
                    PostType = NormalizePostType(request.SuggestedPostType, draftResult.Value.PostType),
                    ResourceList = importedResourceIds.Select(id => id.ToString()).ToList()
                },
                "draft",
                null,
                null,
                PostBuilderOriginKinds.AiOther),
            cancellationToken);

        if (postResult.IsFailure)
        {
            return Result.Failure<ChatWebPostResult>(postResult.Error);
        }

        var postBuilderId = postResult.Value.PostBuilderId;
        if (postBuilderId.HasValue && importedResourceIds.Count > 0)
        {
            var addResourcesResult = await _mediator.Send(
                new AddPostBuilderResourcesCommand(
                    postBuilderId.Value,
                    request.UserId,
                    importedResourceIds),
                cancellationToken);

            if (addResourcesResult.IsFailure)
            {
                return Result.Failure<ChatWebPostResult>(addResourcesResult.Error);
            }
        }

        var sourceUrls = usableSources
            .Select(result => result.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result.Success(new ChatWebPostResult(
            postResult.Value.Id,
            postBuilderId ?? Guid.Empty,
            postResult.Value.Title,
            retrievalMode,
            sourceUrls,
            importedResourceIds));
    }

    private static List<string> ExtractUrls(string prompt)
    {
        return UrlRegex()
            .Matches(prompt)
            .Select(match => match.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}'))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxUrlsPerPrompt)
            .ToList();
    }

    private static string NormalizePostType(string? suggestedPostType, string? generatedPostType)
    {
        var normalized = (suggestedPostType ?? generatedPostType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "reel" or "reels" or "video"
            ? "reels"
            : "posts";
    }

    private static string BuildFallbackTitle(string prompt)
    {
        var compact = string.Join(' ', prompt
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return compact.Length <= 80 ? compact : compact[..80].TrimEnd();
    }

    [GeneratedRegex("https?://[^\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
