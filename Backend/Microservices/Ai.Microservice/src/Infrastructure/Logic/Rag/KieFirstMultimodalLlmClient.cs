using System.Text.Json;
using Application.Abstractions.Rag;
using Application.Abstractions.Search;
using Infrastructure.Logic.Kie;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Rag;

/// <summary>
/// Multimodal chat client that prefers Kie's GPT chat-capable Responses API and
/// falls back to the OpenRouter chat-completions client if Kie rejects or fails.
/// </summary>
public sealed class KieFirstMultimodalLlmClient : IMultimodalLlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly KieResponsesClient _kieResponsesClient;
    private readonly OpenRouterMultimodalLlmClient _openRouterFallback;
    private readonly MultimodalLlmOptions _options;
    private readonly IWebSearchClient _webSearch;
    private readonly ILogger<KieFirstMultimodalLlmClient> _logger;
    private readonly string? _configuredKieModel;

    public KieFirstMultimodalLlmClient(
        KieResponsesClient kieResponsesClient,
        OpenRouterMultimodalLlmClient openRouterFallback,
        MultimodalLlmOptions options,
        IWebSearchClient webSearch,
        IConfiguration configuration,
        ILogger<KieFirstMultimodalLlmClient> logger)
    {
        _kieResponsesClient = kieResponsesClient;
        _openRouterFallback = openRouterFallback;
        _options = options;
        _webSearch = webSearch;
        _logger = logger;
        _configuredKieModel = configuration["Kie:ChatModel"]
                              ?? configuration["Kie__ChatModel"]
                              ?? configuration["Rag:KieChatModel"]
                              ?? configuration["RAG_KIE_CHAT_MODEL"];
    }

    public async Task<MultimodalAnswerResult> GenerateAnswerAsync(
        MultimodalAnswerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateWithKieAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Kie multimodal chat failed; falling back to OpenRouter chat completions.");

            return await _openRouterFallback.GenerateAnswerAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<MultimodalAnswerResult> GenerateWithKieAsync(
        MultimodalAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var imageUrls = NormalizeImageUrls(request.ReferenceImageUrls);
        var model = KieResponsesClient.ResolveResponsesModel(
            request.ModelOverride,
            _configuredKieModel);
        var webSearchEnabled = request.WebSearchEnabled ?? _options.WebSearchEnabled;

        _logger.LogInformation(
            "Kie multimodal chat call: model={Model} systemPromptLen={SysLen} userTextLen={UserLen} images={ImageCount} webSearchEnabled={WebSearch}",
            model,
            request.SystemPrompt?.Length ?? 0,
            request.UserText?.Length ?? 0,
            imageUrls.Count,
            webSearchEnabled);

        var input = new List<KieResponsesInputItem>
        {
            KieResponsesClient.SystemText(request.SystemPrompt ?? string.Empty),
            BuildUserInput(request.UserText ?? string.Empty, imageUrls),
        };
        var sources = new List<WebSource>();
        var roundsRemaining = _options.MaxToolRounds;

        while (true)
        {
            var rawResult = await _kieResponsesClient.CreateRawResponseAsync(
                model,
                input,
                "Kie.MultimodalChatFailed",
                "Kie multimodal chat request failed.",
                cancellationToken,
                webSearchEnabled ? [BuildWebSearchTool()] : null,
                webSearchEnabled ? "auto" : null)
                .ConfigureAwait(false);

            if (rawResult.IsFailure)
            {
                throw new InvalidOperationException(rawResult.Error.Description);
            }

            var calls = KieResponsesClient.ExtractFunctionCalls(rawResult.Value);
            if (webSearchEnabled && roundsRemaining > 0 && calls.Count > 0)
            {
                roundsRemaining--;
                foreach (var call in calls)
                {
                    input.Add(KieResponsesClient.FunctionCall(call.CallId, call.Name, call.Arguments));
                    var (toolOutput, hits) = await ExecuteWebSearchAsync(call, cancellationToken)
                        .ConfigureAwait(false);
                    input.Add(KieResponsesClient.FunctionCallOutput(call.CallId, toolOutput));
                    sources.AddRange(hits.Select(hit => new WebSource(
                        Url: hit.Url,
                        Title: hit.Title,
                        Snippet: hit.Snippet)));
                }
                continue;
            }

            var text = KieResponsesClient.ExtractText(rawResult.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Kie multimodal chat returned an empty response.");
            }

            return new MultimodalAnswerResult(text.Trim(), DeduplicateSources(sources));
        }
    }

    private async Task<(string ToolOutput, IReadOnlyList<WebSearchHit> Hits)> ExecuteWebSearchAsync(
        KieResponsesFunctionCall call,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(call.Name, "web_search", StringComparison.Ordinal))
        {
            _logger.LogWarning("Kie model called unknown tool '{Name}'", call.Name);
            return (
                JsonSerializer.Serialize(new { error = $"unknown tool: {call.Name}" }, JsonOptions),
                Array.Empty<WebSearchHit>());
        }

        var query = TryReadSearchQuery(call.Arguments);
        if (string.IsNullOrWhiteSpace(query))
        {
            return (
                JsonSerializer.Serialize(new { error = "missing or empty 'query'" }, JsonOptions),
                Array.Empty<WebSearchHit>());
        }

        _logger.LogInformation("Kie model invoked web_search(query=\"{Query}\")", query);
        var hits = await _webSearch.SearchAsync(query, _options.WebSearchMaxResults, cancellationToken)
            .ConfigureAwait(false);
        var compact = hits.Select(hit => new
        {
            title = hit.Title,
            url = hit.Url,
            snippet = hit.Snippet,
            age = hit.Age,
        }).ToArray();

        return (JsonSerializer.Serialize(compact, JsonOptions), hits);
    }

    private static KieResponsesInputItem BuildUserInput(
        string userText,
        IReadOnlyList<string> imageUrls)
    {
        var parts = new List<KieResponsesContentPart>
        {
            new() { Type = "input_text", Text = userText }
        };
        foreach (var url in imageUrls)
        {
            parts.Add(new KieResponsesContentPart
            {
                Type = "input_image",
                ImageUrl = url,
            });
        }

        return KieResponsesClient.UserParts(parts);
    }

    private static KieResponsesFunctionTool BuildWebSearchTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "web_search",
            Description =
                "Search the public web for current/fresh information. " +
                "Use ONLY when the question requires recent data: trending topics this week, " +
                "just-released platform features, current news, recent statistics, competitor launches. " +
                "Do NOT use for general best-practices, copywriting formulas, or the user's own past data.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "Concise search query in English. 3-8 words is ideal.",
                    },
                },
                required = new[] { "query" },
            },
        };
    }

    private static string TryReadSearchQuery(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            return document.RootElement.TryGetProperty("query", out var query) &&
                   query.ValueKind == JsonValueKind.String
                ? query.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> NormalizeImageUrls(IReadOnlyList<string>? imageUrls)
    {
        if (imageUrls is null || imageUrls.Count == 0)
        {
            return Array.Empty<string>();
        }

        return imageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .ToArray();
    }

    private static IReadOnlyList<WebSource> DeduplicateSources(IReadOnlyList<WebSource> sources)
    {
        if (sources.Count <= 1)
        {
            return sources;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return sources.Where(source => seen.Add(source.Url)).ToList();
    }
}
