using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Rag;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Rag;

public sealed class MultimodalLlmOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "openai/gpt-4o-mini";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

public sealed class OpenRouterMultimodalLlmClient : IMultimodalLlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly MultimodalLlmOptions _options;
    private readonly ILogger<OpenRouterMultimodalLlmClient> _logger;

    public OpenRouterMultimodalLlmClient(
        HttpClient http,
        MultimodalLlmOptions options,
        ILogger<OpenRouterMultimodalLlmClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<string> GenerateAnswerAsync(
        MultimodalAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var userParts = new List<object>
        {
            new TextPart("text", request.UserText),
        };
        if (request.ReferenceImageUrls != null)
        {
            foreach (var url in request.ReferenceImageUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    userParts.Add(new ImagePart("image_url", new ImageUrl(url)));
                }
            }
        }

        var body = new
        {
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = userParts },
            },
            temperature = 0.4,
        };

        using var response = await _http.PostAsJsonAsync(
            "chat/completions",
            body,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Multimodal LLM call failed: HTTP {Status} body={Body}",
                (int)response.StatusCode,
                raw.Length > 600 ? raw[..600] : raw);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException(
            $"Multimodal LLM response had no message content. Raw: {raw[..Math.Min(raw.Length, 400)]}");
    }

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
