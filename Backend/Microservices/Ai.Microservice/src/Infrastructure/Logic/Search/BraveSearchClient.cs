using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Search;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Search;

public sealed class BraveSearchOptions
{
    public string BaseUrl { get; init; } = "https://api.search.brave.com/res/v1/web/search";
    public string ApiKey { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Calls Brave Search API (api.search.brave.com). Used as the backend for the
/// `web_search` function tool exposed to the answer-generation LLM. Pricing as of
/// 2026: ~$5/mo for 2k queries on the Data for Search Free tier; pay-as-you-go
/// at ~$0.0025 per query above that.
/// </summary>
public sealed class BraveSearchClient : IWebSearchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly BraveSearchOptions _options;
    private readonly ILogger<BraveSearchClient> _logger;

    public BraveSearchClient(
        HttpClient http,
        BraveSearchOptions options,
        ILogger<BraveSearchClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Subscription-Token", _options.ApiKey);
        // Note: not advertising Accept-Encoding: gzip — the default HttpClientHandler
        // doesn't auto-decompress unless AutomaticDecompression is configured, and
        // Brave honors the header literally, returning raw gzip bytes that fail JSON parse.
    }

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<WebSearchHit>();
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Brave Search disabled: no API key configured");
            return Array.Empty<WebSearchHit>();
        }

        var count = Math.Clamp(maxResults, 1, 20);
        var url = $"{_options.BaseUrl}?q={Uri.EscapeDataString(query)}&count={count}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Brave Search HTTP {Status} for query='{Query}': {Body}",
                    (int)response.StatusCode, query, raw[..Math.Min(raw.Length, 300)]);
                return Array.Empty<WebSearchHit>();
            }

            var payload = JsonSerializer.Deserialize<BraveResponse>(raw, JsonOptions);
            var results = payload?.Web?.Results ?? Array.Empty<BraveResult>();
            return results
                .Where(r => !string.IsNullOrWhiteSpace(r.Url))
                .Select(r => new WebSearchHit(
                    Url: r.Url!,
                    Title: r.Title ?? string.Empty,
                    Snippet: r.Description,
                    Age: r.Age))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brave Search failed for query='{Query}'", query);
            return Array.Empty<WebSearchHit>();
        }
    }

    private sealed class BraveResponse
    {
        [JsonPropertyName("web")]
        public BraveWeb? Web { get; set; }
    }

    private sealed class BraveWeb
    {
        [JsonPropertyName("results")]
        public BraveResult[]? Results { get; set; }
    }

    private sealed class BraveResult
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("age")]
        public string? Age { get; set; }
    }
}
