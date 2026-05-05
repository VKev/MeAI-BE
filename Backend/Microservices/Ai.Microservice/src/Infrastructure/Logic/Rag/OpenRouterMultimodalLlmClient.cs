using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Rag;
using Application.Abstractions.Search;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Rag;

public sealed class MultimodalLlmOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "openai/gpt-4o-mini";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// When true, exposes a `web_search` function tool the model can choose to call.
    /// Genuinely conditional — the model only invokes it for queries needing fresh
    /// data. Pays nothing extra for queries the model decides don't need search.
    /// Requires a working <see cref="IWebSearchClient"/> implementation in DI.
    /// </summary>
    public bool WebSearchEnabled { get; init; } = true;

    /// <summary>
    /// Max search results returned to the model per `web_search` invocation. Higher =
    /// more grounded, more prompt tokens. Default 5.
    /// </summary>
    public int WebSearchMaxResults { get; init; } = 5;

    /// <summary>
    /// Max number of tool-call rounds per request. 1 = model can search at most once
    /// before writing the final answer. 2+ = model can chain searches. We cap at 2 to
    /// bound cost; agentic recursion should be opt-in not default.
    /// </summary>
    public int MaxToolRounds { get; init; } = 2;
}

/// <summary>
/// Chat-completion client with optional web-search tool calling.
///
/// Flow: send the LLM call with a `web_search` tool defined. If the response carries
/// `tool_calls`, the runtime executes Brave Search, appends the result as a `tool`
/// message, and calls the LLM again. The model decides whether to search; queries
/// that don't need fresh data return after a single LLM call (no extra cost).
///
/// When the search tool runs, the top results are also surfaced as
/// <see cref="WebSource"/> entries on the result so the frontend can render a
/// "Sources" footer.
/// </summary>
public sealed class OpenRouterMultimodalLlmClient : IMultimodalLlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly MultimodalLlmOptions _options;
    private readonly IWebSearchClient _webSearch;
    private readonly ILogger<OpenRouterMultimodalLlmClient> _logger;

    public OpenRouterMultimodalLlmClient(
        HttpClient http,
        MultimodalLlmOptions options,
        IWebSearchClient webSearch,
        ILogger<OpenRouterMultimodalLlmClient> logger)
    {
        _http = http;
        _options = options;
        _webSearch = webSearch;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<MultimodalAnswerResult> GenerateAnswerAsync(
        MultimodalAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var userParts = new List<object>
        {
            new TextPart("text", request.UserText),
        };
        var includedImages = 0;
        if (request.ReferenceImageUrls != null)
        {
            foreach (var url in request.ReferenceImageUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    userParts.Add(new ImagePart("image_url", new ImageUrl(url)));
                    includedImages++;
                }
            }
        }
        _logger.LogInformation(
            "OpenRouter chat call: model={Model} systemPromptLen={SysLen} userTextLen={UserLen} images={ImageCount} webSearchEnabled={WebSearch}",
            _options.Model,
            request.SystemPrompt?.Length ?? 0,
            request.UserText?.Length ?? 0,
            includedImages,
            _options.WebSearchEnabled);

        // Conversation history as raw JsonObjects so we can append tool / assistant
        // turns without leaking into the typed records.
        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt },
            new { role = "user", content = userParts },
        };

        var sourcesAccumulated = new List<WebSource>();
        var toolRoundsRemaining = _options.MaxToolRounds;

        while (true)
        {
            var body = BuildRequestBody(messages);
            var (msg, _) = await SendChatCompletionAsync(body, cancellationToken);

            // Inspect for tool_calls. If present and we have rounds left + search
            // enabled, execute the search and loop. Otherwise treat as final.
            var toolCalls = ExtractToolCalls(msg);
            if (_options.WebSearchEnabled && toolRoundsRemaining > 0 && toolCalls.Count > 0)
            {
                toolRoundsRemaining--;
                // Append the assistant's tool-call message to history.
                messages.Add(BuildAssistantToolCallMessage(msg, toolCalls));
                // Execute each tool call and append the corresponding tool message.
                foreach (var call in toolCalls)
                {
                    var (toolMessage, hits) = await ExecuteWebSearchAsync(call, cancellationToken);
                    messages.Add(toolMessage);
                    foreach (var hit in hits)
                    {
                        sourcesAccumulated.Add(new WebSource(
                            Url: hit.Url, Title: hit.Title, Snippet: hit.Snippet));
                    }
                }
                continue;
            }

            // Final answer turn — no more tool calls, or we ran out of rounds.
            var answer = msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                ? (content.GetString() ?? string.Empty)
                : string.Empty;

            // Annotation-style citations (some models still emit these even with
            // function calling). Merge with tool-fetched sources and dedupe by URL.
            var annotated = ExtractAnnotationSources(msg);
            var combined = MergeSources(sourcesAccumulated, annotated);
            return new MultimodalAnswerResult(answer, combined);
        }
    }

    private object BuildRequestBody(IReadOnlyList<object> messages)
    {
        if (_options.WebSearchEnabled)
        {
            return new
            {
                model = _options.Model,
                messages,
                temperature = 0.4,
                tools = new object[] { WebSearchToolDefinition },
                tool_choice = "auto",
            };
        }
        return new
        {
            model = _options.Model,
            messages,
            temperature = 0.4,
        };
    }

    private async Task<(JsonElement Message, string RawBody)> SendChatCompletionAsync(
        object body, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "chat/completions", body, JsonOptions, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Multimodal LLM call failed: HTTP {Status} body={Body}",
                (int)response.StatusCode, raw[..Math.Min(raw.Length, 600)]);
            response.EnsureSuccessStatusCode();
        }

        // Note: we clone the JsonElement out of the disposing JsonDocument so the
        // caller can safely use it after this method returns.
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var msg))
        {
            throw new InvalidOperationException(
                $"Multimodal LLM response had no message. Raw: {raw[..Math.Min(raw.Length, 400)]}");
        }
        return (msg.Clone(), raw);
    }

    private static List<ToolCall> ExtractToolCalls(JsonElement msg)
    {
        if (!msg.TryGetProperty("tool_calls", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return new List<ToolCall>(0);
        }
        var calls = new List<ToolCall>();
        foreach (var c in arr.EnumerateArray())
        {
            var id = c.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrEmpty(id)) continue;
            var fn = c.TryGetProperty("function", out var f) ? f : default;
            var name = fn.ValueKind == JsonValueKind.Object && fn.TryGetProperty("name", out var n)
                ? n.GetString() : null;
            var args = fn.ValueKind == JsonValueKind.Object && fn.TryGetProperty("arguments", out var a)
                ? a.GetString() : "{}";
            calls.Add(new ToolCall(id!, name ?? string.Empty, args ?? "{}"));
        }
        return calls;
    }

    private static object BuildAssistantToolCallMessage(JsonElement msg, IReadOnlyList<ToolCall> calls)
    {
        return new
        {
            role = "assistant",
            content = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null,
            tool_calls = calls.Select(c => new
            {
                id = c.Id,
                type = "function",
                function = new { name = c.Name, arguments = c.Arguments },
            }).ToArray(),
        };
    }

    private async Task<(object ToolMessage, IReadOnlyList<WebSearchHit> Hits)> ExecuteWebSearchAsync(
        ToolCall call, CancellationToken cancellationToken)
    {
        if (call.Name != "web_search")
        {
            _logger.LogWarning("Model called unknown tool '{Name}' — returning empty result", call.Name);
            return (new
            {
                role = "tool", tool_call_id = call.Id,
                content = JsonSerializer.Serialize(new { error = $"unknown tool: {call.Name}" }),
            }, Array.Empty<WebSearchHit>());
        }

        string query;
        try
        {
            using var doc = JsonDocument.Parse(call.Arguments);
            query = doc.RootElement.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
                ? (q.GetString() ?? string.Empty)
                : string.Empty;
        }
        catch
        {
            query = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return (new
            {
                role = "tool", tool_call_id = call.Id,
                content = JsonSerializer.Serialize(new { error = "missing or empty 'query'" }),
            }, Array.Empty<WebSearchHit>());
        }

        _logger.LogInformation("LLM invoked web_search(query=\"{Query}\")", query);
        var hits = await _webSearch.SearchAsync(query, _options.WebSearchMaxResults, cancellationToken);

        // Compact JSON shape — keep tokens low. Snippets give the model context
        // to ground answers without us re-fetching the full page.
        var compact = hits.Select(h => new
        {
            title = h.Title,
            url = h.Url,
            snippet = h.Snippet,
            age = h.Age,
        }).ToArray();

        return (new
        {
            role = "tool", tool_call_id = call.Id,
            content = JsonSerializer.Serialize(compact, JsonOptions),
        }, hits);
    }

    /// <summary>
    /// Reads `message.annotations[].url_citation` (older search-preview models still
    /// emit these). Returns sources with character offsets for inline-link rendering.
    /// </summary>
    private static List<WebSource> ExtractAnnotationSources(JsonElement msg)
    {
        var sources = new List<WebSource>();
        if (!msg.TryGetProperty("annotations", out var annotations) ||
            annotations.ValueKind != JsonValueKind.Array)
        {
            return sources;
        }

        foreach (var ann in annotations.EnumerateArray())
        {
            if (!ann.TryGetProperty("type", out var typeNode) ||
                typeNode.GetString() != "url_citation" ||
                !ann.TryGetProperty("url_citation", out var cit))
            {
                continue;
            }

            var url = cit.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) continue;

            var title = cit.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            var startIdx = cit.TryGetProperty("start_index", out var s) && s.ValueKind == JsonValueKind.Number
                ? (int?)s.GetInt32() : null;
            var endIdx = cit.TryGetProperty("end_index", out var e) && e.ValueKind == JsonValueKind.Number
                ? (int?)e.GetInt32() : null;

            sources.Add(new WebSource(url!, title, Snippet: null, StartIndex: startIdx, EndIndex: endIdx));
        }
        return sources;
    }

    private static IReadOnlyList<WebSource> MergeSources(IReadOnlyList<WebSource> a, IReadOnlyList<WebSource> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<WebSource>(a.Count + b.Count);
        foreach (var s in a.Concat(b))
        {
            if (seen.Add(s.Url)) merged.Add(s);
        }
        return merged;
    }

    private static readonly object WebSearchToolDefinition = new
    {
        type = "function",
        function = new
        {
            name = "web_search",
            description =
                "Search the public web for current/fresh information. " +
                "Use ONLY when the question requires recent data: trending topics this week, " +
                "just-released platform features, current news, recent statistics, competitor launches. " +
                "Do NOT use for general best-practices, copywriting formulas, or the user's own past data — " +
                "those are already provided in the prompt context. " +
                "Returns a list of {title, url, snippet, age}; cite the URLs in your final answer.",
            parameters = new
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
        },
    };

    private sealed record ToolCall(string Id, string Name, string Arguments);

    private sealed record TextPart(string Type, string Text)
    {
        [JsonPropertyName("type")] public string Type { get; init; } = Type;
        [JsonPropertyName("text")] public string Text { get; init; } = Text;
    }

    private sealed record ImagePart(string Type, ImageUrl ImageUrl)
    {
        [JsonPropertyName("type")] public string Type { get; init; } = Type;
        [JsonPropertyName("image_url")] public ImageUrl ImageUrl { get; init; } = ImageUrl;
    }

    private sealed record ImageUrl([property: JsonPropertyName("url")] string Url);
}
