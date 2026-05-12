using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Abstractions.Rag;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Rag;

public sealed class RerankOptions
{
    /// <summary>Jina rerank endpoint.</summary>
    public string BaseUrl { get; init; } = "https://api.jina.ai/v1/rerank";

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Jina model id. <c>jina-reranker-m0</c> is the multimodal flagship: it
    /// fetches image URLs and scores their pixel content.
    /// </summary>
    public string Model { get; init; } = "jina-reranker-m0";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Jina-reranker-m0 client. True multimodal cross-encoder: each candidate is sent
/// as either <c>{"image": "url"}</c> or <c>{"text": "..."}</c>. Jina then fetches
/// image URLs and scores actual pixel content against the query.
///
/// Important Jina quirks:
/// 1. A document object passing both <c>image</c> and <c>text</c> fields collapses
///    to text-only scoring, so we deliberately pick one field per candidate.
/// 2. If Jina cannot fetch an image URL, the whole batch can fail with HTTP 400/500.
///    We remove the rejected URL and retry so one bad fresh-search hit does not poison
///    the whole reference selection pass.
/// </summary>
public sealed class JinaRerankClient : IRerankClient
{
    private const int MaxRejectedImageUrlRetries = 5;

    private static readonly Regex UnloadableImageUrlRegex = new(
        @"The URL (?<url>https?://[^\s'""}]+) cannot be loaded",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        try
        {
            var activeDocuments = documents
                .Select((document, index) => new IndexedRerankDocument(index, document))
                .ToList();

            for (var attempt = 0; attempt <= MaxRejectedImageUrlRetries; attempt++)
            {
                var (docArray, indexMap) = BuildDocumentPayload(activeDocuments);
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
                    var rejectedUrl = TryFindRejectedImageUrl(raw, activeDocuments);
                    if (!string.IsNullOrWhiteSpace(rejectedUrl) &&
                        attempt < MaxRejectedImageUrlRetries &&
                        RemoveImageUrl(activeDocuments, rejectedUrl))
                    {
                        _logger.LogWarning(
                            "Jina rerank rejected image URL; retrying without it (model={Model} status={Status} url={Url} remainingDocs={RemainingDocs})",
                            _options.Model,
                            (int)response.StatusCode,
                            rejectedUrl[..Math.Min(rejectedUrl.Length, 200)],
                            activeDocuments.Count);
                        continue;
                    }

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

            return Array.Empty<RerankResult>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jina rerank call failed for {DocCount} documents", documents.Count);
            return Array.Empty<RerankResult>();
        }
    }

    private static (JsonArray Documents, List<int> IndexMap) BuildDocumentPayload(
        IReadOnlyList<IndexedRerankDocument> documents)
    {
        var docArray = new JsonArray();
        var indexMap = new List<int>(documents.Count);

        foreach (var indexedDocument in documents)
        {
            var d = indexedDocument.Document;
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
            indexMap.Add(indexedDocument.OriginalIndex);
        }

        return (docArray, indexMap);
    }

    private static bool RemoveImageUrl(List<IndexedRerankDocument> documents, string imageUrl)
    {
        var removed = false;
        for (var i = documents.Count - 1; i >= 0; i--)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(documents[i].Document.ImageUrl, imageUrl))
            {
                continue;
            }

            documents.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    private static string? TryFindRejectedImageUrl(
        string responseBody,
        IReadOnlyList<IndexedRerankDocument> documents)
    {
        foreach (var url in documents
                     .Select(d => d.Document.ImageUrl)
                     .Where(url => !string.IsNullOrWhiteSpace(url))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(url => url!.Length))
        {
            if (responseBody.Contains(url!, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
        }

        var match = UnloadableImageUrlRegex.Match(responseBody);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["url"].Value.TrimEnd('.', ',', ';');
    }

    private sealed record IndexedRerankDocument(int OriginalIndex, RerankDocument Document);

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
