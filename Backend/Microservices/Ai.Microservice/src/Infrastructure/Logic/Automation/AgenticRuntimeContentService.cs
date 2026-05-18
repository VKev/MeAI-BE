using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Regex MarkdownImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownHeadingRegex = new(@"^\s{0,3}#{1,6}\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownQuoteRegex = new(@"^\s{0,3}>\s?", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownListRegex = new(@"^\s{0,3}(?:[-*+]|\d+\.)\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownInlineRegex = new(@"(?<!\w)(?:\*\*|__|\*|_|~~|`)+|(?:\*\*|__|\*|_|~~|`)+(?!\w)", RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);

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
            var availableResources = BuildResourceCatalog(request.Search.ImportedResources);
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
                    content must be plain text suitable for a social post. Do not use markdown headings, bullet lists, markdown links, bold, italics, code fences, or blockquotes.
                    Respect maxContentLength as a hard character cap when it is provided.
                    If the payload includes recommendationSummary or recommendationPageProfile, use them to match the account's voice, positioning, and contact details.
                    Keep the post grounded in fresh search results when they are present.
                    Use import_media when web images/videos should be attached to the resulting post.
                    Media is opt-in: only attach media by listing the exact resourceIds you want in create_runtime_post_draft.
                    Never attach duplicate, broken, off-topic, logo-only, or low-information images.
                    Default to zero or one attached media item unless the target explicitly requires something else.

                    """ + BuildPrompt(request))
            };

            var runtimeDraft = await RunToolLoopAsync(
                request,
                model,
                input,
                availableResources,
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

        return KieResponsesClient.ResolveResponsesModel(
            activeConfigResult.IsSuccess ? activeConfigResult.Value?.ChatModel : null,
            configuredModel);
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
            publishingConstraints = new
            {
                desiredPostType = request.DesiredPostType,
                requiresVideoMedia = request.RequiresVideoMedia,
                requiresSingleMedia = request.RequiresSingleMedia,
                allowTextOnly = request.AllowTextOnly,
                targetInstructionSummary = request.TargetInstructionSummary
            },
            search = request.Search
        }, JsonOptions);

        return
            "Create one plain-text social post for immediate scheduled publishing from this payload. " +
            "If recommendationSummary is present, treat it as the primary brand-voice and page-profile grounding. " +
            "Use the web search payload for freshness and facts. " +
            "Do not format the caption as markdown. " +
            "If you attach media, include only the chosen resourceIds in create_runtime_post_draft; unlisted media will be ignored. " +
            $"The final postType must be \"{NormalizePostType(request.DesiredPostType)}\". " +
            (request.RequiresVideoMedia == true
                ? "You must select exactly one VIDEO resource and align the draft for short-form video publishing. "
                : string.Empty) +
            (request.RequiresSingleMedia == true && request.RequiresVideoMedia != true
                ? "You must select exactly one media resource. "
                : string.Empty) +
            (request.AllowTextOnly == false && request.RequiresVideoMedia != true
                ? "Do not finalize the draft without required media. "
                : string.Empty) +
            "If maxContentLength is set, keep content within that hard limit. Return one publishable post only.\n\n" +
            payload;
    }

    private async Task<AgenticRuntimePostDraft?> RunToolLoopAsync(
        AgenticRuntimeContentRequest request,
        string model,
        List<KieResponsesInputItem> input,
        Dictionary<Guid, ImportedResourceItem> availableResources,
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

                    var resourceIds = ResolveFinalResourceIds(parsed.ResourceIds, request, availableResources);
                    var resources = resourceIds
                        .Select(resourceId => new AgenticRuntimeDraftResource(
                            resourceId,
                            availableResources.GetValueOrDefault(resourceId)?.ResourceType))
                        .ToList();

                    return parsed with
                    {
                        ResourceIds = resourceIds.Count > 0 ? resourceIds : null,
                        Resources = resources.Count > 0 ? resources : null
                    };
                }

                var toolOutput = await ExecuteToolCallAsync(
                    request,
                    call,
                    availableResources,
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
        Dictionary<Guid, ImportedResourceItem> availableResources,
        CancellationToken cancellationToken)
    {
        return call.Name switch
        {
            "web_search" => await ExecuteWebSearchAsync(request, call.Arguments, availableResources, cancellationToken),
            "fetch_url" => await ExecuteFetchUrlAsync(request, call.Arguments, availableResources, cancellationToken),
            "import_media" => await ExecuteImportMediaAsync(request, call.Arguments, availableResources, cancellationToken),
            _ => new { error = $"Unsupported tool: {call.Name}" }
        };
    }

    private async Task<object> ExecuteWebSearchAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        Dictionary<Guid, ImportedResourceItem> availableResources,
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

        MergeImportedResources(availableResources, result.Value.ImportedResources);
        return BuildSearchToolOutput(result.Value);
    }

    private async Task<object> ExecuteFetchUrlAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        Dictionary<Guid, ImportedResourceItem> availableResources,
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

        MergeImportedResources(availableResources, result.ImportedResources);
        return BuildSearchToolOutput(result);
    }

    private async Task<object> ExecuteImportMediaAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        Dictionary<Guid, ImportedResourceItem> availableResources,
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
        var createdResourceIds = new List<Guid>();
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
                    availableResources[resource.ResourceId] = new ImportedResourceItem(
                        resource.ResourceId,
                        resource.PresignedUrl,
                        resource.ContentType,
                        resource.ResourceType,
                        resource.OriginSourceUrl ?? group.First().Url,
                        null);
                    createdResourceIds.Add(resource.ResourceId);
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
            resourceIds = createdResourceIds.Distinct().ToList()
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

    private static void MergeImportedResources(
        Dictionary<Guid, ImportedResourceItem> availableResources,
        IReadOnlyList<ImportedResourceItem>? importedResources)
    {
        if (importedResources is null)
        {
            return;
        }

        foreach (var item in importedResources.Where(item => item.ResourceId != Guid.Empty))
        {
            availableResources[item.ResourceId] = item;
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
                        @enum = new[] { "posts", "reels" },
                        description = "Runtime schedule post type."
                    },
                    resourceIds = new
                    {
                        type = new[] { "array", "null" },
                        description = "Optional explicit list of imported resourceIds to attach. Only list the exact resources that should be published.",
                        items = new { type = "string", format = "uuid" }
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
                string.IsNullOrWhiteSpace(parsed.PostType) ? "posts" : parsed.PostType.Trim(),
                parsed.ResourceIds?.Where(id => id != Guid.Empty).Distinct().ToList());
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
        var availableResources = BuildResourceCatalog(request.Search.ImportedResources);
        var resourceIds = ResolveFinalResourceIds(null, request, availableResources);
        var resources = resourceIds
            .Select(resourceId => new AgenticRuntimeDraftResource(
                resourceId,
                availableResources.GetValueOrDefault(resourceId)?.ResourceType))
            .ToList();

        return new AgenticRuntimePostDraft(
            title,
            string.IsNullOrWhiteSpace(content) ? request.Search.Query : content,
            null,
            NormalizePostType(request.DesiredPostType),
            resourceIds.Count > 0 ? resourceIds : null,
            resources.Count > 0 ? resources : null);
    }

    private static AgenticRuntimePostDraft ApplyContentLimit(AgenticRuntimePostDraft draft, int? maxContentLength)
    {
        draft = SanitizePlainTextDraft(draft);

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

    private static string NormalizePostType(string? postType)
    {
        return string.Equals((postType ?? string.Empty).Trim(), "reels", StringComparison.OrdinalIgnoreCase)
            ? "reels"
            : "posts";
    }

    private static AgenticRuntimePostDraft SanitizePlainTextDraft(AgenticRuntimePostDraft draft)
    {
        return draft with
        {
            Title = SanitizePlainText(draft.Title),
            Content = SanitizePlainText(draft.Content) ?? string.Empty
        };
    }

    private static string? SanitizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value?.Trim();
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        normalized = MarkdownImageRegex.Replace(normalized, "$1");
        normalized = MarkdownLinkRegex.Replace(normalized, "$1");
        normalized = MarkdownHeadingRegex.Replace(normalized, string.Empty);
        normalized = MarkdownQuoteRegex.Replace(normalized, string.Empty);
        normalized = MarkdownListRegex.Replace(normalized, string.Empty);
        normalized = MarkdownInlineRegex.Replace(normalized, string.Empty);
        normalized = ExcessBlankLinesRegex.Replace(normalized, "\n\n");

        var cleanedLines = normalized
            .Split('\n')
            .Select(line => line.TrimEnd());

        return string.Join('\n', cleanedLines).Trim();
    }

    private static Dictionary<Guid, ImportedResourceItem> BuildResourceCatalog(
        IReadOnlyList<ImportedResourceItem>? importedResources)
    {
        var availableResources = new Dictionary<Guid, ImportedResourceItem>();
        MergeImportedResources(availableResources, importedResources);
        return availableResources;
    }

    private static List<Guid> ResolveFinalResourceIds(
        IReadOnlyList<Guid>? explicitResourceIds,
        AgenticRuntimeContentRequest request,
        IReadOnlyDictionary<Guid, ImportedResourceItem> availableResources)
    {
        var explicitSelection = explicitResourceIds?
            .Where(id => id != Guid.Empty && availableResources.ContainsKey(id))
            .Distinct()
            .ToList() ?? [];

        if (explicitSelection.Count > 0)
        {
            return explicitSelection;
        }

        if (request.RequiresVideoMedia != true &&
            request.RequiresSingleMedia != true &&
            request.AllowTextOnly != false)
        {
            return [];
        }

        var compatible = availableResources.Values
            .Where(item => item.ResourceId != Guid.Empty && IsCompatibleResource(item.ResourceType, request))
            .Select(item => item.ResourceId)
            .Distinct()
            .ToList();

        return compatible.Count == 1 ? compatible : [];
    }

    private static bool IsCompatibleResource(
        string? resourceType,
        AgenticRuntimeContentRequest request)
    {
        if (request.RequiresVideoMedia == true)
        {
            return IsVideoResource(resourceType);
        }

        if (request.RequiresSingleMedia == true || request.AllowTextOnly == false)
        {
            return IsImageResource(resourceType) || IsVideoResource(resourceType);
        }

        return false;
    }

    private static bool IsImageResource(string? resourceType)
        => string.Equals(resourceType, "image", StringComparison.OrdinalIgnoreCase);

    private static bool IsVideoResource(string? resourceType)
        => string.Equals(resourceType, "video", StringComparison.OrdinalIgnoreCase);

    private sealed class AgenticRuntimePostDraftPayload
    {
        public string? Title { get; set; }

        public string? Content { get; set; }

        public string? Hashtag { get; set; }

        public string? PostType { get; set; }

        public List<Guid>? ResourceIds { get; set; }
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
