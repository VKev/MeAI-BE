using System.Text.Json;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using Infrastructure.Logic.Kie;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.Resources;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Automation;

public sealed class AgenticRuntimeContentService : IAgenticRuntimeContentService
{
    private const int MaxToolTurns = 12;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfiguration _configuration;
    private readonly IUserConfigService _userConfigService;
    private readonly ILogger<AgenticRuntimeContentService> _logger;
    private readonly KieResponsesClient _kieResponsesClient;
    private readonly IAgentWebSearchService _agentWebSearchService;
    private readonly IWebSearchEnrichmentService _webSearchEnrichmentService;
    private readonly IUserResourceService _userResourceService;

    public AgenticRuntimeContentService(
        IConfiguration configuration,
        KieResponsesClient kieResponsesClient,
        IAgentWebSearchService agentWebSearchService,
        IWebSearchEnrichmentService webSearchEnrichmentService,
        IUserResourceService userResourceService,
        IUserConfigService userConfigService,
        ILogger<AgenticRuntimeContentService> logger)
    {
        _configuration = configuration;
        _kieResponsesClient = kieResponsesClient;
        _agentWebSearchService = agentWebSearchService;
        _webSearchEnrichmentService = webSearchEnrichmentService;
        _userResourceService = userResourceService;
        _userConfigService = userConfigService;
        _logger = logger;
    }

    public async Task<Result<AgenticRuntimePostDraft>> GeneratePostDraftAsync(
        AgenticRuntimeContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await ResolveModelAsync(cancellationToken);
            var initialResourceIds = request.Search.ImportedResources?
                .Select(item => item.ResourceId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList() ?? [];
            var input = new List<KieResponsesInputItem>
            {
                KieResponsesClient.UserText(
                    """
                    You create concise social media post drafts from verified web search results and optional RAG recommendation grounding.
                    Available tools:
                    - web_search: search the web for more current sources.
                    - fetch_url: fetch and enrich specific URLs.
                    - import_media: import image/video URLs into the MeAI resource system.
                    - create_runtime_post_draft: finalize the draft output.
                    Always finish by calling create_runtime_post_draft. Do not answer in plain text.
                    The final postType must be "posts".
                    content must be plain text suitable for a social post.
                    Respect maxContentLength as a hard character cap when it is provided.
                    If the payload includes recommendationSummary or recommendationPageProfile, use them to match the account's voice, positioning, and contact details.
                    Keep the post grounded in fresh search results when they are present.
                    Use import_media when web images/videos should be attached to the resulting post.

                    """ + BuildPrompt(request))
            };

            var runtimeDraft = await RunToolLoopAsync(
                request,
                model,
                input,
                initialResourceIds,
                cancellationToken);
            if (runtimeDraft is not null && !string.IsNullOrWhiteSpace(runtimeDraft.Content))
            {
                return Result.Success(ApplyContentLimit(runtimeDraft, request.MaxContentLength));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kie runtime content generation failed for ScheduleId {ScheduleId}", request.ScheduleId);
        }

        return Result.Success(ApplyContentLimit(CreateFallbackDraft(request), request.MaxContentLength));
    }

    private async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var activeConfigResult = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        var configuredModel = _configuration["Kie:ChatModel"]
                              ?? _configuration["Kie__ChatModel"];

        if (activeConfigResult.IsSuccess &&
            !string.IsNullOrWhiteSpace(activeConfigResult.Value?.ChatModel))
        {
            return activeConfigResult.Value.ChatModel.Trim();
        }

        return string.IsNullOrWhiteSpace(configuredModel)
            ? KieResponsesClient.DefaultChatModel
            : configuredModel.Trim();
    }

    private static string BuildPrompt(AgenticRuntimeContentRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            scheduleId = request.ScheduleId,
            scheduleName = request.ScheduleName,
            agentPrompt = request.AgentPrompt,
            platformPreference = request.PlatformPreference,
            maxContentLength = request.MaxContentLength,
            grounding = new
            {
                socialMediaId = request.GroundingSocialMediaId,
                platform = request.GroundingPlatform,
                recommendationQuery = request.RecommendationQuery,
                recommendationSummary = request.RecommendationSummary,
                recommendationPageProfile = request.RecommendationPageProfile,
                recommendationWebSources = request.RecommendationWebSources,
                ragFallbackReason = request.RagFallbackReason
            },
            search = request.Search
        }, JsonOptions);

        return
            "Create one plain-text social post for immediate scheduled publishing from this payload. " +
            "If recommendationSummary is present, treat it as the primary brand-voice and page-profile grounding. " +
            "Use the web search payload for freshness and facts. If maxContentLength is set, keep content within that hard limit. Return one publishable post only.\n\n" +
            payload;
    }

    private async Task<AgenticRuntimePostDraft?> RunToolLoopAsync(
        AgenticRuntimeContentRequest request,
        string model,
        List<KieResponsesInputItem> input,
        List<Guid> importedResourceIds,
        CancellationToken cancellationToken)
    {
        var tools = new KieResponsesTool[]
        {
            BuildWebSearchTool(),
            BuildFetchUrlTool(),
            BuildImportMediaTool(),
            BuildRuntimeDraftTool()
        };

        for (var turn = 0; turn < MaxToolTurns; turn++)
        {
            var rawResult = await _kieResponsesClient.CreateRawResponseAsync(
                model,
                input,
                "AgenticRuntime.RequestFailed",
                "Kie runtime content generation failed.",
                cancellationToken,
                tools);
            if (rawResult.IsFailure)
            {
                return null;
            }

            var calls = KieResponsesClient.ExtractFunctionCalls(rawResult.Value);
            if (calls.Count == 0)
            {
                return null;
            }

            foreach (var call in calls)
            {
                if (string.Equals(call.Name, "create_runtime_post_draft", StringComparison.Ordinal))
                {
                    var parsed = TryParseDraft(call.Arguments);
                    if (parsed is null)
                    {
                        return null;
                    }

                    return parsed with { ResourceIds = importedResourceIds.Distinct().ToList() };
                }

                var toolOutput = await ExecuteToolCallAsync(
                    request,
                    call,
                    importedResourceIds,
                    cancellationToken);

                input.Add(KieResponsesClient.FunctionCall(call.CallId, call.Name, call.Arguments));
                input.Add(KieResponsesClient.FunctionCallOutput(
                    call.CallId,
                    JsonSerializer.Serialize(toolOutput, JsonOptions)));
            }
        }

        return null;
    }

    private async Task<object> ExecuteToolCallAsync(
        AgenticRuntimeContentRequest request,
        KieResponsesFunctionCall call,
        List<Guid> importedResourceIds,
        CancellationToken cancellationToken)
    {
        return call.Name switch
        {
            "web_search" => await ExecuteWebSearchAsync(request, call.Arguments, importedResourceIds, cancellationToken),
            "fetch_url" => await ExecuteFetchUrlAsync(request, call.Arguments, importedResourceIds, cancellationToken),
            "import_media" => await ExecuteImportMediaAsync(request, call.Arguments, importedResourceIds, cancellationToken),
            _ => new { error = $"Unsupported tool: {call.Name}" }
        };
    }

    private async Task<object> ExecuteWebSearchAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<WebSearchToolArguments>(arguments, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Query))
        {
            return new { error = "web_search requires a non-empty query." };
        }

        var result = await _agentWebSearchService.SearchAsync(
            new AgentWebSearchRequest(
                payload.Query.Trim(),
                Math.Clamp(payload.Count ?? 5, 1, 10),
                payload.Country,
                payload.SearchLanguage,
                payload.Freshness,
                request.UserId,
                request.WorkspaceId,
                request.OriginChatSessionId,
                request.OriginChatId),
            cancellationToken);

        if (result.IsFailure)
        {
            return new { error = result.Error.Description };
        }

        MergeImportedResourceIds(importedResourceIds, result.Value.ImportedResources);
        return BuildSearchToolOutput(result.Value);
    }

    private async Task<object> ExecuteFetchUrlAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<FetchUrlToolArguments>(arguments, JsonOptions);
        var urls = payload?.Urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList() ?? [];

        if (urls.Count == 0)
        {
            return new { error = "fetch_url requires at least one URL." };
        }

        var result = await _webSearchEnrichmentService.EnrichUrlsAsync(
            urls,
            payload?.Query ?? request.AgentPrompt ?? request.Search.Query,
            request.UserId,
            request.WorkspaceId,
            request.OriginChatSessionId,
            request.OriginChatId,
            cancellationToken);

        MergeImportedResourceIds(importedResourceIds, result.ImportedResources);
        return BuildSearchToolOutput(result);
    }

    private async Task<object> ExecuteImportMediaAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ImportMediaToolArguments>(arguments, JsonOptions);
        var urls = payload?.Urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList() ?? [];

        if (!request.UserId.HasValue || urls.Count == 0)
        {
            return new { error = "import_media requires authenticated runtime context and at least one URL." };
        }

        var imported = new List<object>();
        foreach (var group in urls
                     .Select(url => new { Url = url, ResourceType = ClassifyMediaType(url) })
                     .Where(item => item.ResourceType is not null)
                     .GroupBy(item => item.ResourceType!, StringComparer.OrdinalIgnoreCase))
        {
            var createResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                request.UserId.Value,
                group.Select(item => item.Url).ToList(),
                "ready",
                group.Key,
                cancellationToken,
                request.WorkspaceId,
                new ResourceProvenanceMetadata(
                    ResourceOriginKinds.AiImportedUrl,
                    request.OriginChatSessionId,
                    request.OriginChatId));

            if (createResult.IsFailure)
            {
                imported.Add(new { error = createResult.Error.Description, resourceType = group.Key });
                continue;
            }

            foreach (var resource in createResult.Value)
            {
                if (resource.ResourceId != Guid.Empty)
                {
                    importedResourceIds.Add(resource.ResourceId);
                }

                imported.Add(new
                {
                    resourceId = resource.ResourceId,
                    presignedUrl = resource.PresignedUrl,
                    contentType = resource.ContentType,
                    resourceType = resource.ResourceType
                });
            }
        }

        return new
        {
            importedResources = imported,
            resourceIds = importedResourceIds.Distinct().ToList()
        };
    }

    private static object BuildSearchToolOutput(AgentWebSearchResponse response)
    {
        return new
        {
            query = response.Query,
            retrievedAtUtc = response.RetrievedAtUtc,
            llmContext = response.LlmContext,
            results = response.Results.Select(result => new
            {
                title = result.Title,
                pageTitle = result.PageTitle,
                url = result.Url,
                description = result.Description,
                source = result.Source,
                pageContent = result.PageContent,
                mediaUrls = result.MediaUrls
            }).ToList(),
            importedResources = response.ImportedResources?.Select(item => new
            {
                resourceId = item.ResourceId,
                presignedUrl = item.PresignedUrl,
                contentType = item.ContentType,
                resourceType = item.ResourceType,
                sourceUrl = item.SourceUrl,
                sourcePageUrl = item.SourcePageUrl
            }).ToList()
        };
    }

    private static void MergeImportedResourceIds(
        List<Guid> importedResourceIds,
        IReadOnlyList<ImportedResourceItem>? importedResources)
    {
        if (importedResources is null)
        {
            return;
        }

        foreach (var resourceId in importedResources
                     .Select(item => item.ResourceId)
                     .Where(id => id != Guid.Empty))
        {
            importedResourceIds.Add(resourceId);
        }
    }

    private static KieResponsesFunctionTool BuildWebSearchTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "web_search",
            Description = "Search the public web and enrich the top results with page content and discovered media URLs.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties = new
                {
                    query = new { type = "string" },
                    count = new { type = new[] { "integer", "null" } },
                    country = new { type = new[] { "string", "null" } },
                    searchLanguage = new { type = new[] { "string", "null" } },
                    freshness = new { type = new[] { "string", "null" } }
                }
            }
        };
    }

    private static KieResponsesFunctionTool BuildFetchUrlTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "fetch_url",
            Description = "Fetch and enrich one or more direct URLs with page content and media URLs.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "urls" },
                properties = new
                {
                    urls = new
                    {
                        type = "array",
                        items = new { type = "string" }
                    },
                    query = new { type = new[] { "string", "null" } }
                }
            }
        };
    }

    private static KieResponsesFunctionTool BuildImportMediaTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "import_media",
            Description = "Import web image or video URLs into the MeAI user resource system so they can be attached to the final post.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "urls" },
                properties = new
                {
                    urls = new
                    {
                        type = "array",
                        items = new { type = "string" }
                    }
                }
            }
        };
    }

    private static KieResponsesFunctionTool BuildRuntimeDraftTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "create_runtime_post_draft",
            Description = "Create one runtime social media post draft from the schedule payload.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "title", "content", "hashtag", "postType" },
                properties = new
                {
                    title = new
                    {
                        type = new[] { "string", "null" },
                        description = "Short draft title, or null."
                    },
                    content = new
                    {
                        type = "string",
                        description = "Plain text social post content."
                    },
                    hashtag = new
                    {
                        type = new[] { "string", "null" },
                        description = "Optional hashtag string, or null."
                    },
                    postType = new
                    {
                        type = "string",
                        @enum = new[] { "posts" },
                        description = "Runtime schedule post type."
                    }
                }
            }
        };
    }

    private static AgenticRuntimePostDraft? TryParseDraft(string raw)
    {
        var normalized = raw.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            normalized = normalized.Trim('`').Trim();
            if (normalized.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[4..].Trim();
            }
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AgenticRuntimePostDraftPayload>(normalized, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Content))
            {
                return null;
            }

            return new AgenticRuntimePostDraft(
                parsed.Title?.Trim(),
                parsed.Content.Trim(),
                string.IsNullOrWhiteSpace(parsed.Hashtag) ? null : parsed.Hashtag.Trim(),
                string.IsNullOrWhiteSpace(parsed.PostType) ? "posts" : parsed.PostType.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgenticRuntimePostDraft CreateFallbackDraft(AgenticRuntimeContentRequest request)
    {
        var topResult = request.Search.Results.FirstOrDefault();
        var title = request.ScheduleName ?? topResult?.Title ?? "Runtime update";
        var content = string.Join(
            "\n",
            new[]
            {
                request.RecommendationSummary,
                request.AgentPrompt,
                topResult?.Title,
                topResult?.Description,
                topResult?.Url
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new AgenticRuntimePostDraft(
            title,
            string.IsNullOrWhiteSpace(content) ? request.Search.Query : content,
            null,
            "posts",
            request.Search.ImportedResources?
                .Select(item => item.ResourceId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList());
    }

    private static AgenticRuntimePostDraft ApplyContentLimit(AgenticRuntimePostDraft draft, int? maxContentLength)
    {
        if (!maxContentLength.HasValue || maxContentLength.Value < 1)
        {
            return draft;
        }

        var trimmedContent = TrimToLength(draft.Content, maxContentLength.Value);
        var trimmedTitle = TrimToLength(draft.Title, Math.Min(maxContentLength.Value, 120));
        var trimmedHashtag = TrimToLength(draft.Hashtag, Math.Min(maxContentLength.Value, 200));

        return draft with
        {
            Title = trimmedTitle,
            Content = trimmedContent ?? string.Empty,
            Hashtag = trimmedHashtag
        };
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd();
    }

    private sealed class AgenticRuntimePostDraftPayload
    {
        public string? Title { get; set; }

        public string? Content { get; set; }

        public string? Hashtag { get; set; }

        public string? PostType { get; set; }
    }

    private sealed class WebSearchToolArguments
    {
        public string? Query { get; set; }
        public int? Count { get; set; }
        public string? Country { get; set; }
        public string? SearchLanguage { get; set; }
        public string? Freshness { get; set; }
    }

    private sealed class FetchUrlToolArguments
    {
        public List<string>? Urls { get; set; }
        public string? Query { get; set; }
    }

    private sealed class ImportMediaToolArguments
    {
        public List<string>? Urls { get; set; }
    }

    private static string? ClassifyMediaType(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        var extension = Path.GetExtension(path).Trim().ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" or ".avif" => "image",
            ".mp4" or ".mov" or ".webm" or ".m4v" or ".avi" or ".mkv" or ".mpeg" or ".mpg" => "video",
            _ when url.Contains("/image", StringComparison.OrdinalIgnoreCase) => "image",
            _ when url.Contains("/video", StringComparison.OrdinalIgnoreCase) => "video",
            _ => null
        };
    }
}
