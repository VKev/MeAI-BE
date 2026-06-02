using System.Net.Http;
using System.Text.Json;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Application.Abstractions.Rag;
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
    private readonly IImageGenerationClient _imageGenerationClient;
    private readonly IMultimodalLlmClient _multimodalLlmClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public AgenticRuntimeContentService(
        IConfiguration configuration,
        KieResponsesClient kieResponsesClient,
        IAgentWebSearchService agentWebSearchService,
        IWebSearchEnrichmentService webSearchEnrichmentService,
        IUserResourceService userResourceService,
        IImageGenerationClient imageGenerationClient,
        IMultimodalLlmClient multimodalLlmClient,
        IHttpClientFactory httpClientFactory,
        IUserConfigService userConfigService,
        ILogger<AgenticRuntimeContentService> logger)
    {
        _configuration = configuration;
        _kieResponsesClient = kieResponsesClient;
        _agentWebSearchService = agentWebSearchService;
        _webSearchEnrichmentService = webSearchEnrichmentService;
        _userResourceService = userResourceService;
        _imageGenerationClient = imageGenerationClient;
        _multimodalLlmClient = multimodalLlmClient;
        _httpClientFactory = httpClientFactory;
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
            var importedResourceTypes = new Dictionary<Guid, string?>();
            var initialResourceIds = request.Search.ImportedResources?
                .Select(item => item.ResourceId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList() ?? [];
            MergeImportedResourceTypes(importedResourceTypes, request.Search.ImportedResources);
            var input = new List<KieResponsesInputItem>
            {
                KieResponsesClient.UserText(
                    """
                    You create concise social media post drafts from verified web search results and optional RAG recommendation grounding.
                    Available tools:
                    - web_search: search the web for more current sources.
                    - fetch_url: fetch and enrich specific URLs.
                    - validate_media: check whether a list of image/video URLs are publicly accessible AND whether each image is visually suitable for the post topic.
                      ALWAYS call this for web image URLs before importing them.
                      Returns per-URL: status (ok/error), content-type, and for images: suitability (suitable/unsuitable) + reason.
                      Only import images whose suitability is "suitable". If unsuitable, call generate_image instead.
                      Videos (generated from prompt or imported) are always suitable — you do NOT need to validate_media for AI-generated content.
                    - import_media: import one or more validated image/video URLs into the MeAI resource system so they can be attached to the post.
                      Only call this for URLs that passed validate_media with suitability="suitable" (or for video URLs).
                    - generate_image: generate a brand-new image from a text prompt when web images are unavailable, or validate_media marked them as unsuitable.
                      Prefer this for single decorative images (Instagram/Facebook posts, TikTok single-image posts).
                      For AI scheduled photo posts, create or import one suitable image.
                    - create_runtime_post_draft: finalize the draft output.
                    Always finish by calling create_runtime_post_draft. Do not answer in plain text.
                    CRITICAL: Do NOT call create_runtime_post_draft in the same turn as other tools (like web_search, fetch_url, validate_media, import_media, generate_image). You must call those other tools first, wait for their outputs to be returned to you in the next turn, and only call create_runtime_post_draft in a subsequent, final turn with the final content and imported resource IDs.
                    content must be plain text suitable for a social post.
                    Respect maxContentLength as a hard character cap when it is provided.
                    If the payload includes recommendationSummary or recommendationPageProfile, use them to match the account's voice, positioning, and contact details.
                    Keep the post grounded in fresh search results when they are present.
                    Workflow for images: web_search → validate_media (ALWAYS for web images) → import_media (only suitable ones) OR generate_image (if none suitable) → create_runtime_post_draft.
                    TikTok photo posts (postType=posts): validate web images via validate_media, import one suitable image, OR call generate_image once. Do NOT import a video.
                    TikTok reels (postType=reels): find and import exactly ONE VIDEO URL from web_search. generate_image only produces STILL IMAGES and CANNOT create videos, so do NOT use it for reels. If no public video URL is found in web search results, call create_runtime_post_draft with postType=reels and no resourceIds — the system will handle the failure gracefully.

                    """ + BuildPrompt(request))
            };

            var runtimeDraft = await RunToolLoopAsync(
                request,
                model,
                input,
                initialResourceIds,
                importedResourceTypes,
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
            $"The final postType must be \"{NormalizePostType(request.DesiredPostType)}\". " +
            BuildMediaInstruction(request) +
            "If maxContentLength is set, keep content within that hard limit. Return one publishable post only.\n\n" +
            payload;
    }

    /// <summary>
    /// Returns a platform-aware media instruction string appended to the per-request prompt.
    /// </summary>
    private static string BuildMediaInstruction(AgenticRuntimeContentRequest request)
    {
        var platform = (request.GroundingPlatform ?? request.PlatformPreference ?? string.Empty)
            .Trim().ToLowerInvariant();
        var postType = NormalizePostType(request.DesiredPostType);

        // TikTok photo carousel
        if (string.Equals(platform, "tiktok", StringComparison.Ordinal) &&
            string.Equals(postType, "posts", StringComparison.Ordinal))
        {
            return
                "This is a TikTok PHOTO CAROUSEL post. " +
                "First, validate web image URLs via validate_media and import the suitable ones via import_media. " +
                "If no suitable web images are found, call generate_image (one call per slide) to create images instead. " +
                "Attach exactly one image in total. Do NOT import or reference a video. Do NOT finalize without one image resource. ";
        }

        // TikTok reels (video)
        if (string.Equals(platform, "tiktok", StringComparison.Ordinal) &&
            string.Equals(postType, "reels", StringComparison.Ordinal))
        {
            return
                "This is a TikTok REELS post. " +
                "You MUST find and import exactly ONE VIDEO resource from web_search results. " +
                "IMPORTANT: generate_image only produces STILL IMAGES and CANNOT create video files — do NOT call generate_image for a reels post. " +
                "If no suitable video URL is found in web_search results, still call create_runtime_post_draft with postType=reels and an empty resourceIds list — the system will report the missing video. ";
        }

        // Instagram posts (Requires exactly one media, text-only NOT allowed)
        if (string.Equals(platform, "instagram", StringComparison.Ordinal) &&
            string.Equals(postType, "posts", StringComparison.Ordinal))
        {
            return
                "This is an Instagram post. You MUST attach exactly one media resource (image or video). " +
                "First, try to find and validate a relevant web image/video URL via validate_media and import it via import_media if suitable. " +
                "If no suitable web media is found, you MUST call generate_image to create a single high-quality decorative image for the post. ";
        }

        // Platforms that allow text-only but support media (Facebook, Threads, and any generic/custom platform posts)
        if (string.Equals(postType, "posts", StringComparison.Ordinal) &&
            !string.Equals(platform, "tiktok", StringComparison.Ordinal) &&
            !string.Equals(platform, "instagram", StringComparison.Ordinal))
        {
            var capitalizedPlatform = string.IsNullOrEmpty(platform) ? "social media" : char.ToUpper(platform[0]) + platform[1..];
            return
                $"This is a {capitalizedPlatform} post. While text-only posts are allowed, you should highly prefer generating or importing a suitable image to make the post visually engaging and ensure it has resources. " +
                "First, try to find and validate a relevant web image URL via validate_media and import it via import_media if suitable. " +
                "If no suitable web images are found, ALWAYS call generate_image to create a single high-quality decorative image for the post. ";
        }

        // Generic video-required (Facebook/Instagram reels)
        if (request.RequiresVideoMedia == true)
        {
            return
                "You must find and import exactly ONE VIDEO resource from web_search results. " +
                "IMPORTANT: generate_image only produces STILL IMAGES and CANNOT create video files \u2014 do NOT call generate_image for a reels/video post. " +
                "If no suitable video URL is found, still call create_runtime_post_draft with no resourceIds \u2014 the system will report the missing video. ";
        }

        // Single media required (AI scheduled posts)
        if (request.RequiresSingleMedia == true)
        {
            return "You must attach exactly one media resource. ";
        }

        // Media required but not single (shouldn't happen often outside TikTok carousel)
        if (request.AllowTextOnly == false)
        {
            return "Do not finalize the draft without required media. ";
        }

        return string.Empty;
    }

    private async Task<AgenticRuntimePostDraft?> RunToolLoopAsync(
        AgenticRuntimeContentRequest request,
        string model,
        List<KieResponsesInputItem> input,
        List<Guid> importedResourceIds,
        Dictionary<Guid, string?> importedResourceTypes,
        CancellationToken cancellationToken)
    {
        var tools = new KieResponsesTool[]
        {
            BuildWebSearchTool(),
            BuildFetchUrlTool(),
            BuildValidateMediaTool(),
            BuildImportMediaTool(),
            BuildGenerateImageTool(),
            BuildRuntimeDraftTool()
        };

        for (var turn = 0; turn < MaxToolTurns; turn++)
        {
            // Use "required" on every turn so the model is forced to call a tool.
            // Without this, the model can respond with plain text after web_search
            // and the loop terminates prematurely.
            var rawResult = await _kieResponsesClient.CreateRawResponseAsync(
                model,
                input,
                "AgenticRuntime.RequestFailed",
                "Kie runtime content generation failed.",
                cancellationToken,
                tools,
                toolChoice: "required");
            if (rawResult.IsFailure)
            {
                return null;
            }

            var calls = KieResponsesClient.ExtractFunctionCalls(rawResult.Value);
            if (calls.Count == 0)
            {
                // Unexpected — model ignored tool_choice=required.
                // Log and bail so we use the fallback draft.
                _logger.LogWarning(
                    "AgenticRuntime turn {Turn}: model returned no tool calls despite tool_choice=required. Response preview: {Preview}",
                    turn,
                    rawResult.Value.Length > 500 ? rawResult.Value[..500] : rawResult.Value);
                return null;
            }

            var hasOtherTools = calls.Any(c => !string.Equals(c.Name, "create_runtime_post_draft", StringComparison.Ordinal));
            foreach (var call in calls)
            {
                if (string.Equals(call.Name, "create_runtime_post_draft", StringComparison.Ordinal))
                {
                    if (hasOtherTools)
                    {
                        _logger.LogWarning(
                            "AgenticRuntime turn {Turn}: create_runtime_post_draft was called prematurely alongside other tools in the same turn. Intercepting to prevent empty resource draft.",
                            turn);
                        input.Add(KieResponsesClient.FunctionCall(call.CallId, call.Name, call.Arguments));
                        input.Add(KieResponsesClient.FunctionCallOutput(call.CallId,
                            "{\"error\": \"Do not call create_runtime_post_draft in the same turn as other tools (like web_search, validate_media, import_media, generate_image). You must call those other tools, wait for their outputs to be returned to you, and then call create_runtime_post_draft in a subsequent turn with the imported resource IDs.\"}"));
                        continue;
                    }

                    var parsed = TryParseDraft(call.Arguments);
                    if (parsed is not null)
                    {
                        var resourceIds = importedResourceIds.Distinct().ToList();
                        var resources = resourceIds
                            .Select(resourceId => new AgenticRuntimeDraftResource(
                                resourceId,
                                importedResourceTypes.GetValueOrDefault(resourceId)))
                            .ToList();

                        return parsed with
                        {
                            ResourceIds = resourceIds,
                            Resources = resources
                        };
                    }

                    // Parse failed — ask the model to try again with valid JSON.
                    _logger.LogWarning(
                        "AgenticRuntime turn {Turn}: create_runtime_post_draft arguments failed to parse. Args: {Args}",
                        turn,
                        call.Arguments.Length > 500 ? call.Arguments[..500] : call.Arguments);

                    input.Add(KieResponsesClient.FunctionCall(call.CallId, call.Name, call.Arguments));
                    input.Add(KieResponsesClient.FunctionCallOutput(call.CallId,
                        "{\"error\": \"Invalid JSON in draft arguments. Call create_runtime_post_draft again with properly formatted JSON fields: title, content, hashtag, postType.\"}"));
                    break; // restart outer loop so model retries
                }

                var toolOutput = await ExecuteToolCallAsync(
                    request,
                    call,
                    importedResourceIds,
                    importedResourceTypes,
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
        Dictionary<Guid, string?> importedResourceTypes,
        CancellationToken cancellationToken)
    {
        return call.Name switch
        {
            "web_search"    => await ExecuteWebSearchAsync(request, call.Arguments, importedResourceIds, importedResourceTypes, cancellationToken),
            "fetch_url"     => await ExecuteFetchUrlAsync(request, call.Arguments, importedResourceIds, importedResourceTypes, cancellationToken),
            "import_media"  => await ExecuteImportMediaAsync(request, call.Arguments, importedResourceIds, importedResourceTypes, cancellationToken),
            "validate_media" => await ExecuteValidateMediaAsync(request, call.Arguments, cancellationToken),
            "generate_image" => await ExecuteGenerateImageAsync(request, call.Arguments, importedResourceIds, importedResourceTypes, cancellationToken),
            _ => new { error = $"Unsupported tool: {call.Name}" }
        };
    }

    private async Task<object> ExecuteWebSearchAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        Dictionary<Guid, string?> importedResourceTypes,
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
        MergeImportedResourceTypes(importedResourceTypes, result.Value.ImportedResources);
        return BuildSearchToolOutput(result.Value);
    }

    private async Task<object> ExecuteFetchUrlAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        Dictionary<Guid, string?> importedResourceTypes,
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
        MergeImportedResourceTypes(importedResourceTypes, result.ImportedResources);
        return BuildSearchToolOutput(result);
    }

    private async Task<object> ExecuteImportMediaAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        Dictionary<Guid, string?> importedResourceTypes,
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
                    importedResourceTypes[resource.ResourceId] = resource.ResourceType;
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

    /// <summary>
    /// Validates whether media URLs are publicly reachable via HTTP HEAD.
    /// For image URLs that pass the accessibility check, additionally calls a vision LLM
    /// to assess whether the image content is visually suitable for the post topic.
    /// Video URLs (generated from prompt or imported) are always marked as suitable —
    /// their content matches intent by construction.
    /// </summary>
    private async Task<object> ExecuteValidateMediaAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ValidateMediaToolArguments>(arguments, JsonOptions);
        var urls = payload?.Urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(35)
            .ToList() ?? [];

        if (urls.Count == 0)
        {
            return new { error = "validate_media requires at least one URL." };
        }

        _logger.LogInformation("validate_media: validating {Count} URLs in parallel...", urls.Count);

        using var http = _httpClientFactory.CreateClient("AgentValidation");
        http.Timeout = TimeSpan.FromSeconds(10);

        var tasks = urls.Select(async url =>
        {
            HttpResponseMessage? resp = null;
            try
            {
                // Try HEAD request first with standard desktop browser User-Agent
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("validate_media HEAD request threw an exception for {Url}: {Message}", url, ex.Message);
                }

                // If HEAD fails or returns non-success (e.g. 403, 405), fallback immediately to GET (headers only)
                if (resp == null || !resp.IsSuccessStatusCode)
                {
                    resp?.Dispose();
                    _logger.LogDebug("validate_media: HEAD failed or was unsuccessful for {Url}. Retrying with GET...", url);
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }

                using (resp)
                {
                    var contentType = resp.Content.Headers.ContentType?.MediaType;
                    var contentLength = resp.Content.Headers.ContentLength;
                    var isImage = contentType != null &&
                                  contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                    var isVideo = contentType != null &&
                                  contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
                    var isMedia = isImage || isVideo;

                    if (!resp.IsSuccessStatusCode || !isMedia)
                    {
                        _logger.LogInformation("validate_media: URL {Url} is not accessible or not media. Status: {StatusCode}, Content-Type: {ContentType}", url, resp.StatusCode, contentType ?? "unknown");
                        return new
                        {
                            url,
                            status = (int)resp.StatusCode,
                            ok = false,
                            contentType = contentType ?? "unknown",
                            contentLengthBytes = contentLength,
                            suitability = "unknown",
                            suitabilityReason = (string?)null,
                            hint = !resp.IsSuccessStatusCode
                                ? "URL not accessible — skip or generate instead."
                                : $"Content-type '{contentType}' is not a media type — skip or generate instead."
                        };
                    }

                    // Heuristic checks to filter out generic web elements, tiny card thumbnails, publisher logos, etc.
                    if (isImage && IsLikelyJunkOrLogo(url, contentLength, out var junkReason))
                    {
                        _logger.LogInformation("validate_media: URL {Url} marked as unsuitable via heuristics: {Reason}", url, junkReason);
                        return new
                        {
                            url,
                            status = (int)resp.StatusCode,
                            ok = true,
                            contentType = contentType!,
                            contentLengthBytes = contentLength,
                            suitability = "unsuitable",
                            suitabilityReason = (string?)junkReason,
                            hint = $"Filtered out by heuristic checks: {junkReason}. Use generate_image instead."
                        };
                    }

                    // Videos are always suitable — generated from prompt or known source.
                    if (isVideo)
                    {
                        _logger.LogInformation("validate_media: URL {Url} is a video. Marking as suitable.", url);
                        return new
                        {
                            url,
                            status = (int)resp.StatusCode,
                            ok = true,
                            contentType = contentType!,
                            contentLengthBytes = contentLength,
                            suitability = "suitable",
                            suitabilityReason = (string?)"Video content is always considered suitable.",
                            hint = "URL is accessible and the video is suitable for import."
                        };
                    }

                    // Images: send to vision LLM for suitability check.
                    _logger.LogInformation("validate_media: URL {Url} passed heuristics, calling vision LLM...", url);
                    var (suitability, suitabilityReason) = await CheckImageSuitabilityAsync(
                        url, request, cancellationToken);

                    _logger.LogInformation("validate_media: URL {Url} evaluated by vision LLM as {Suitability} ({Reason})", url, suitability, suitabilityReason);

                    return new
                    {
                        url,
                        status = (int)resp.StatusCode,
                        ok = true,
                        contentType = contentType!,
                        contentLengthBytes = contentLength,
                        suitability,
                        suitabilityReason = (string?)suitabilityReason,
                        hint = string.Equals(suitability, "suitable", StringComparison.Ordinal)
                            ? "Image is accessible and suitable for the post — safe to import."
                            : $"Image is accessible but NOT suitable: {suitabilityReason}. Use generate_image instead."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("validate_media: URL {Url} failed check: {Message}", url, ex.Message);
                resp?.Dispose();
                return new
                {
                    url,
                    status = 0,
                    ok = false,
                    contentType = "unknown",
                    contentLengthBytes = (long?)null,
                    suitability = "unknown",
                    suitabilityReason = (string?)null,
                    hint = $"Request failed: {ex.Message}. Skip or generate instead."
                };
            }
        });

        var resultsArray = await Task.WhenAll(tasks);
        var results = resultsArray.ToList();

        _logger.LogInformation("validate_media: completed validation of {Count} URLs.", urls.Count);

        return new { validationResults = results };
    }

    /// <summary>
    /// Calls the vision LLM to assess whether the image at <paramref name="imageUrl"/>
    /// is visually appropriate and relevant for the current post context.
    /// Returns ("suitable" | "unsuitable", reason).
    /// On any failure, defaults to "suitable" so a transient LLM error never blocks import.
    /// </summary>
    private async Task<(string Suitability, string Reason)> CheckImageSuitabilityAsync(
        string imageUrl,
        AgenticRuntimeContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var topic = !string.IsNullOrWhiteSpace(request.AgentPrompt)
                ? request.AgentPrompt.Length > 200 ? request.AgentPrompt[..200] : request.AgentPrompt
                : "a social media post";

            var platform = (request.GroundingPlatform ?? request.PlatformPreference ?? "social media")
                .Trim();

            var visionResult = await _multimodalLlmClient.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt:
                        "You are an expert media editor and content quality evaluator. " +
                        "Analyze the provided image and determine if it is visually suitable and highly relevant " +
                        "to be published on a social media post for the given topic.\n" +
                        "Strictly reject and mark as UNSUITABLE if the image meets any of the following criteria:\n" +
                        "1. Is a generic logo, app icon, website header, newspaper banner (e.g. VnExpress logo, Nhandan banner, etc.), user interface screenshot, app advertisement, or publisher branding element.\n" +
                        "2. Is a stock-like generic photo of people, hands, office desks, devices, or general office setups (e.g. generic hands holding a smartphone, abstract laptop typing, group of generic office workers smiling or looking at a screen) that are low-value filler rather than representing the actual specific topic content. Reject people in poses unless the post topic is specifically about those exact, identifiable individuals.\n" +
                        "3. Is a generic high-tech illustration, abstract concept visual, or digital placeholder art (e.g., 3D renderings of glowing brains, neon network lines, binary code streams, robot/humanoid hands touching screens, VR headsets, generic circuit boards) rather than a real-world photo or specific diagram representing a concrete new release.\n" +
                        "4. Lacks a direct, high-value visual connection to the specific news, entities, products, or events in the post topic.\n\n" +
                        "Only mark as SUITABLE if the image is high-quality, authentic, and directly illustrates the specific content described in the topic.\n\n" +
                        "Respond with exactly one line in this format: " +
                        "SUITABLE: <brief explanation why it is a perfect match> or UNSUITABLE: <explicit reason why it is generic, a logo, or unrelated>.",
                    UserText:
                        $"Post topic: {topic}\n" +
                        $"Target platform: {platform}\n" +
                        "Is this image suitable to publish with this post? Reply SUITABLE or UNSUITABLE with a brief reason.",
                    ReferenceImageUrls: [imageUrl],
                    MaxOutputTokens: 80,
                    WebSearchEnabled: false),
                cancellationToken);

            var answer = (visionResult.Answer ?? string.Empty).Trim();
            if (answer.StartsWith("SUITABLE", StringComparison.OrdinalIgnoreCase))
            {
                var reason = answer.Length > 9 && answer[8] == ':'
                    ? answer[9..].Trim()
                    : "Visually relevant and appropriate for the post topic.";
                return ("suitable", reason);
            }

            if (answer.StartsWith("UNSUITABLE", StringComparison.OrdinalIgnoreCase))
            {
                var reason = answer.Length > 11 && answer[10] == ':'
                    ? answer[11..].Trim()
                    : "Image does not match the post topic or is otherwise inappropriate.";
                return ("unsuitable", reason);
            }

            // Unexpected format — default to unsuitable to be safe and prioritize high-value image generation.
            _logger.LogDebug(
                "[AgenticRuntime] CheckImageSuitability: unexpected vision response '{Answer}' for {Url}",
                answer, imageUrl);
            return ("unsuitable", "Vision check returned an unexpected format; defaulting to unsuitable.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AgenticRuntime] CheckImageSuitability failed or timed out for {Url}; defaulting to unsuitable.", imageUrl);
            return ("unsuitable", $"Vision check failed/timed out: {ex.Message}. Defaulting to unsuitable to prioritize high-value image generation fallback.");
        }
    }

    /// <summary>
    /// Heuristics to screen out generic publisher logos, app icons, website banners,
    /// tiny list card thumbnails, advertisements, and low-value generic assets.
    /// </summary>
    private static bool IsLikelyJunkOrLogo(string url, long? contentLengthBytes, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "URL is empty.";
            return true;
        }

        // 1. Content size threshold: Very small assets (< 12KB) are almost always icons, tiny card thumbnails, logos or placeholder graphics.
        if (contentLengthBytes.HasValue && contentLengthBytes.Value > 0 && contentLengthBytes.Value < 12000)
        {
            reason = $"Image size is too small ({contentLengthBytes.Value} bytes). Likely a logo, icon, or generic spacer.";
            return true;
        }

        try
        {
            var uri = new Uri(url);
            var pathAndQuery = uri.PathAndQuery.ToLowerInvariant();

            // 2. Keyword heuristic checks in path and query
            var junkKeywords = new[]
            {
                "/logo", "logo.", "logo-", "_logo", "-logo",
                "/banner", "banner.", "banner-",
                "/header", "header.", "header-",
                "/icon", "icon.", "icon-", "_icon",
                "/avatar", "avatar.",
                "/footer",
                "ad-placeholder", "advertisement", "/ads/",
                "default-share", "og-image", "thumbnail-default",
                "favicon", "/nav-", "navigation", "button",
                "watermark", "signature", "/branding/",
                "share", "social", "og_image", "placeholder", "default", "fallback", "no-image", "no_image",
                "screenshot", "man-hinh", "man_hinh", "manhinh", "anh-man-hinh", "anh_man_hinh",
                "minh-hoa", "minh_hoa", "minhhoa", "illustration", "stock", "filler", "clipart", "vector",
                "quang-cao", "quang_cao", "quangcao", "advert"
            };

            foreach (var keyword in junkKeywords)
            {
                if (pathAndQuery.Contains(keyword))
                {
                    reason = $"URL path or query contains matching junk/logo keyword: '{keyword}'.";
                    return true;
                }
            }

            // 3. Aspect ratio / dimension query parameter heuristics (e.g. w=300, width=300)
            // Newspaper sites like VnExpress use w=300 for tiny list thumbnails, but w=680+ for main images.
            var query = uri.Query.ToLowerInvariant();
            if (query.Contains("w=") || query.Contains("width="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(query, @"[?&](?:w|width)=(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var width))
                {
                    if (width < 450)
                    {
                        reason = $"Image width parameter is too small ({width}px). Likely a tiny list thumbnail or icon.";
                        return true;
                    }
                }
            }

            if (query.Contains("h=") || query.Contains("height="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(query, @"[?&](?:h|height)=(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var height))
                {
                    if (height > 0 && height < 250)
                    {
                        reason = $"Image height parameter is too small ({height}px). Likely a tiny list thumbnail or icon.";
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Fallthrough if Uri parsing fails for non-standard formats
            reason = $"Failed to parse URL for heuristics: {ex.Message}";
        }

        return false;
    }

    /// <summary>
    /// Generates a new image via <see cref="IImageGenerationClient"/>, uploads the resulting
    /// data URL as a user resource, and returns the resourceId so it can be included in the draft.
    /// </summary>
    private async Task<object> ExecuteGenerateImageAsync(
        AgenticRuntimeContentRequest request,
        string arguments,
        List<Guid> importedResourceIds,
        Dictionary<Guid, string?> importedResourceTypes,
        CancellationToken cancellationToken)
    {
        if (!request.UserId.HasValue)
        {
            return new { error = "generate_image requires authenticated runtime context." };
        }

        var payload = JsonSerializer.Deserialize<GenerateImageToolArguments>(arguments, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Prompt))
        {
            return new { error = "generate_image requires a non-empty prompt." };
        }

        // Build the final prompt (append styleHint if provided)
        var finalPrompt = string.IsNullOrWhiteSpace(payload.StyleHint)
            ? payload.Prompt.Trim()
            : $"{payload.Prompt.Trim()}. Style: {payload.StyleHint.Trim()}";

        // Cap reference images to 3
        var referenceUrls = payload.ReferenceImageUrls?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Take(3)
            .ToList();

        try
        {
            _logger.LogInformation(
                "[AgenticRuntime] generate_image: prompt={Prompt} refImages={RefCount}",
                finalPrompt.Length > 120 ? finalPrompt[..120] + "..." : finalPrompt,
                referenceUrls?.Count ?? 0);

            var genResult = await _imageGenerationClient.GenerateImageAsync(
                new ImageGenerationRequest(finalPrompt, referenceUrls),
                cancellationToken);

            // Upload the generated provider URL or inline data URL into the user resource system.
            var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                request.UserId.Value,
                new[] { genResult.Url },
                status: "ready",
                resourceType: "image",
                cancellationToken,
                workspaceId: request.WorkspaceId);

            if (uploadResult.IsFailure)
            {
                _logger.LogWarning("[AgenticRuntime] generate_image upload failed: {Err}", uploadResult.Error.Description);
                return new { error = $"Image generated but upload failed: {uploadResult.Error.Description}" };
            }

            var uploaded = new List<object>();
            foreach (var res in uploadResult.Value)
            {
                if (res.ResourceId != Guid.Empty)
                {
                    importedResourceIds.Add(res.ResourceId);
                    importedResourceTypes[res.ResourceId] = res.ResourceType ?? "image";
                }

                uploaded.Add(new
                {
                    resourceId = res.ResourceId,
                    presignedUrl = res.PresignedUrl,
                    contentType = res.ContentType,
                    resourceType = res.ResourceType
                });
            }

            _logger.LogInformation(
                "[AgenticRuntime] generate_image: uploaded {Count} resource(s). Cost=${Cost}",
                uploaded.Count,
                genResult.CostUsd?.ToString("F4") ?? "?");

            return new
            {
                generatedImages = uploaded,
                promptUsed = finalPrompt,
                costUsd = genResult.CostUsd
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgenticRuntime] generate_image failed for prompt={Prompt}", finalPrompt);
            return new { error = $"Image generation failed: {ex.Message}. Try import_media or adjust the prompt." };
        }
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

    private static void MergeImportedResourceTypes(
        Dictionary<Guid, string?> importedResourceTypes,
        IReadOnlyList<ImportedResourceItem>? importedResources)
    {
        if (importedResources is null)
        {
            return;
        }

        foreach (var item in importedResources.Where(item => item.ResourceId != Guid.Empty))
        {
            importedResourceTypes[item.ResourceId] = item.ResourceType;
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
            Description =
                "Import one or more web image or video URLs into the MeAI user resource system so they can be attached to the final post. " +
                "For TikTok photo carousels (postType=posts) pass all image URLs in a single call (1–35 URLs). " +
                "For video posts (reels) pass exactly one video URL.",
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
                        items = new { type = "string" },
                        description = "List of publicly accessible image or video URLs to import.",
                        minItems = 1,
                        maxItems = 35
                    }
                }
            }
        };
    }

    private static KieResponsesFunctionTool BuildValidateMediaTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "validate_media",
            Description =
                "Check whether one or more image/video URLs are publicly accessible AND evaluate image suitability for the post topic via vision AI. " +
                "ALWAYS call this for web image URLs before import_media. " +
                "Response per URL includes: ok (bool), contentType, suitability ('suitable'|'unsuitable'|'unknown'), suitabilityReason, and hint. " +
                "Only import images with suitability='suitable'. If unsuitable, call generate_image instead. " +
                "Video URLs are always marked suitable — no need to validate AI-generated or known video URLs.",
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
                        items = new { type = "string" },
                        description = "Image or video URLs to validate. For images, vision AI will check if the content is suitable for the post.",
                        minItems = 1,
                        maxItems = 35
                    }
                }
            }
        };
    }

    private static KieResponsesFunctionTool BuildGenerateImageTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "generate_image",
            Description =
                "Generate a brand-new image using an AI image-generation model. " +
                "Use this when: (a) no suitable web images were found, (b) validate_media reported URLs as inaccessible, or (c) the post needs a custom illustration. " +
                "The generated image is automatically uploaded and returned as a resource you can attach. " +
                "For a TikTok carousel you may call this multiple times (one per slide) or include multiple reference images.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "prompt" },
                properties = new
                {
                    prompt = new
                    {
                        type = "string",
                        description = "Detailed visual description of the image to generate. Be specific about subject, style, lighting, mood."
                    },
                    referenceImageUrls = new
                    {
                        type = new[] { "array", "null" },
                        items = new { type = "string" },
                        description = "Optional: URLs of reference images to guide the visual style (e.g., brand images already imported). Max 3."
                    },
                    styleHint = new
                    {
                        type = new[] { "string", "null" },
                        description = "Optional style instruction appended to the prompt (e.g., 'photorealistic', 'minimalist flat design', 'vibrant editorial')."
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
            NormalizePostType(request.DesiredPostType),
            request.Search.ImportedResources?
                .Select(item => item.ResourceId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList(),
            request.Search.ImportedResources?
                .Where(item => item.ResourceId != Guid.Empty)
                .Select(item => new AgenticRuntimeDraftResource(item.ResourceId, item.ResourceType))
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

    private static string NormalizePostType(string? postType)
    {
        return string.Equals((postType ?? string.Empty).Trim(), "reels", StringComparison.OrdinalIgnoreCase)
            ? "reels"
            : "posts";
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

    private sealed class ValidateMediaToolArguments
    {
        public List<string>? Urls { get; set; }
    }

    private sealed class GenerateImageToolArguments
    {
        public string? Prompt { get; set; }
        public List<string>? ReferenceImageUrls { get; set; }
        public string? StyleHint { get; set; }
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
