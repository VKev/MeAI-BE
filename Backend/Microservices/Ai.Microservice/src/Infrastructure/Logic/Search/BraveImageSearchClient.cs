using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Search;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Search;

public sealed class BraveImageSearchOptions
{
    public string BaseUrl { get; init; } = "https://api.search.brave.com/res/v1/images/search";
    public string ApiKey { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Brave images API requires this as a country code (e.g. "us", "vn").</summary>
    public string Country { get; init; } = "us";

    /// <summary>"strict" | "moderate" | "off". "strict" is the safest default.</summary>
    public string SafeSearch { get; init; } = "strict";
}

/// <summary>
/// Calls Brave Search images endpoint (api.search.brave.com/res/v1/images/search).
/// Used to fetch a fresh real-world visual reference for the topic being drafted —
/// the image-gen model gets these alongside the brand's past-post images so it has
/// a concrete subject anchor (e.g. an actual DJI Osmo product shot when the topic
/// is about that camera).
///
/// Same Brave subscription key as <see cref="BraveSearchClient"/>; just a different
/// endpoint. Pricing: similar tier ($3 / 1000 queries on Data for Search Free).
/// </summary>
public sealed class BraveImageSearchClient : IImageSearchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly BraveImageSearchOptions _options;
    private readonly ILogger<BraveImageSearchClient> _logger;

    public BraveImageSearchClient(
        HttpClient http,
        BraveImageSearchOptions options,
        ILogger<BraveImageSearchClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Subscription-Token", _options.ApiKey);
        // Same gzip caveat as the text client — don't advertise Accept-Encoding,
        // otherwise Brave returns raw gzip and JSON parse fails.
    }

    public async Task<IReadOnlyList<ImageSearchHit>> SearchImagesAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ImageSearchHit>();
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Brave Image Search disabled: no API key configured");
            return Array.Empty<ImageSearchHit>();
        }

        var count = Math.Clamp(maxResults, 1, 100);
        var url =
            $"{_options.BaseUrl}?q={Uri.EscapeDataString(query)}" +
            $"&count={count}" +
            $"&country={Uri.EscapeDataString(_options.Country)}" +
            $"&safesearch={Uri.EscapeDataString(_options.SafeSearch)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Brave Image Search HTTP {Status} for query='{Query}': {Body}",
                    (int)response.StatusCode, query, raw[..Math.Min(raw.Length, 300)]);
                return Array.Empty<ImageSearchHit>();
            }

            var payload = JsonSerializer.Deserialize<BraveImagesResponse>(raw, JsonOptions);
            var results = payload?.Results ?? Array.Empty<BraveImageResult>();
            var hits = new List<ImageSearchHit>(results.Length);
            foreach (var r in results)
            {
                // The actual full-resolution image URL is in `properties.url` per Brave's API.
                // `thumbnail.src` is a Brave-CDN-served small variant (less likely to be hot-link
                // blocked, but lower quality). Prefer the full URL when present.
                var imgUrl = r.Properties?.Url ?? r.Thumbnail?.Src;
                if (string.IsNullOrWhiteSpace(imgUrl))
                {
                    continue;
                }
                hits.Add(new ImageSearchHit(
                    ImageUrl: imgUrl!,
                    ThumbnailUrl: r.Thumbnail?.Src,
                    SourcePageUrl: r.Url,
                    Title: r.Title,
                    Width: r.Properties?.Width,
                    Height: r.Properties?.Height));
            }
            return hits;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brave Image Search failed for query='{Query}'", query);
            return Array.Empty<ImageSearchHit>();
        }
    }

    private sealed class BraveImagesResponse
    {
        [JsonPropertyName("results")]
        public BraveImageResult[]? Results { get; set; }
    }

    private sealed class BraveImageResult
    {
        /// <summary>The page the image appears on.</summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("thumbnail")]
        public BraveImageThumbnail? Thumbnail { get; set; }

        [JsonPropertyName("properties")]
        public BraveImageProperties? Properties { get; set; }
    }

    private sealed class BraveImageThumbnail
    {
        [JsonPropertyName("src")]
        public string? Src { get; set; }
    }

    private sealed class BraveImageProperties
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }
    }
}
