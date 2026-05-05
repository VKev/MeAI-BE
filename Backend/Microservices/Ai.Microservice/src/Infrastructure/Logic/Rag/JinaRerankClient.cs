using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Application.Abstractions.Rag;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Rag;

public sealed class RerankOptions
{
    /// <summary>Jina rerank endpoint.</summary>
    public string BaseUrl { get; init; } = "https://api.jina.ai/v1/rerank";

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Jina model id. <c>jina-reranker-m0</c> is the multimodal flagship —
    /// genuinely fetches image URLs and scores their pixel content (verified
    /// experimentally; unlike Cohere's <c>/v2/rerank</c> which silently text-only-scores).
    /// </summary>
    public string Model { get; init; } = "jina-reranker-m0";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Jina-reranker-m0 client. True multimodal cross-encoder: each candidate is sent
/// as EITHER <c>{"image": "url"}</c> OR <c>{"text": "..."}</c> — Jina then fetches
/// image URLs and scores actual pixel content against the query.
///
/// Important Jina quirks (verified against the live API):
///  1. A document object passing BOTH <c>image</c> AND <c>text</c> fields collapses
///     to text-only scoring (image is ignored). So we deliberately pick ONE field
///     per candidate — image when the candidate is a visual reference, text otherwise.
///  2. If Jina cannot fetch an image URL the WHOLE batch fails with HTTP 400. There
///     is no per-document failure mode. We treat any 400 as a rerank-failed signal
///     and return empty so the consumer falls back to the un-reranked ordering.
///  3. Jina respects scraper-friendly hosts (S3, Github raw, most CDNs) but rejects
///     some sources (Wikipedia /commons/ blocks Jina's UA).
/// </summary>
public sealed class JinaRerankClient : IRerankClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly RerankOptions _options;
    private readonly ILogger<JinaRerankClient> _logger;

    public JinaRerankClient(
        HttpClient http,
        RerankOptions options,
        ILogger<JinaRerankClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<RerankDocument> documents,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || documents.Count == 0)
        {
            return Array.Empty<RerankResult>();
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Rerank disabled: no Jina API key configured");
            return Array.Empty<RerankResult>();
        }

        // Build the documents array. Per Jina semantics: emit ONE field per doc.
        // Prefer image when a visual ref exists (we want the cross-encoder to score
        // pixel content); fall back to text when no image is available.
        //
        // We track an apiIndex → originalIndex map so we can map results back even
        // when some input candidates were skipped (empty Text + empty ImageUrl).
        var docArray = new JsonArray();
        var indexMap = new List<int>(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            var d = documents[i];
            JsonObject? doc = null;
            if (!string.IsNullOrWhiteSpace(d.ImageUrl))
            {
                doc = new JsonObject { ["image"] = d.ImageUrl };
            }
            else if (!string.IsNullOrWhiteSpace(d.Text))
            {
                doc = new JsonObject { ["text"] = d.Text };
            }
            if (doc is null)
            {
                continue;
            }
            docArray.Add(doc);
            indexMap.Add(i);
        }
        if (docArray.Count == 0)
        {
            return Array.Empty<RerankResult>();
        }

        var bodyNode = new JsonObject
        {
            ["model"] = _options.Model,
            ["query"] = query,
            ["documents"] = docArray,
            ["return_documents"] = false,
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = JsonContent.Create(bodyNode, options: JsonOptions),
            };
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var raw = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Jina aborts the batch on any unfetchable image URL with a 400.
                // Log + bail; consumer falls back to the original candidate order.
                _logger.LogWarning(
                    "Jina rerank HTTP {Status} (model={Model} docs={DocCount}): {Body}",
                    (int)response.StatusCode, _options.Model, docArray.Count,
                    raw[..Math.Min(raw.Length, 500)]);
                return Array.Empty<RerankResult>();
            }

            var payload = JsonSerializer.Deserialize<JinaRerankResponse>(raw, JsonOptions);
            var results = payload?.Results ?? Array.Empty<JinaRerankResultDto>();

            var mapped = new List<RerankResult>(results.Length);
            foreach (var r in results)
            {
                if (r.Index < 0 || r.Index >= indexMap.Count) continue;
                mapped.Add(new RerankResult(indexMap[r.Index], r.RelevanceScore));
            }
            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jina rerank call failed for {DocCount} documents", documents.Count);
            return Array.Empty<RerankResult>();
        }
    }

    private sealed class JinaRerankResponse
    {
        [JsonPropertyName("results")]
        public JinaRerankResultDto[]? Results { get; set; }
    }

    private sealed class JinaRerankResultDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; set; }
    }
}
