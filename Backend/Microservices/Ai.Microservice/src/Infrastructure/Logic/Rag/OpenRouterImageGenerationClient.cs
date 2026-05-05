using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Rag;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Rag;

public sealed class ImageGenerationOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "openai/gpt-5.4-image-2";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Calls OpenRouter chat/completions with an image-output model (gpt-5.4-image-2 by default).
/// The model returns the generated image either inside choices[0].message.images[*].image_url.url
/// (data URL) or embedded directly in choices[0].message.content.
/// </summary>
public sealed class OpenRouterImageGenerationClient : IImageGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ImageGenerationOptions _options;
    private readonly ILogger<OpenRouterImageGenerationClient> _logger;

    public OpenRouterImageGenerationClient(
        HttpClient http,
        ImageGenerationOptions options,
        ILogger<OpenRouterImageGenerationClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<ImageGenerationResult> GenerateImageAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var userParts = new List<object> { new TextPart(request.Prompt) };
        var includedImages = 0;
        if (request.ReferenceImageUrls != null)
        {
            foreach (var url in request.ReferenceImageUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    userParts.Add(new ImagePart(new ImageUrl(url)));
                    includedImages++;
                }
            }
        }
        _logger.LogInformation(
            "OpenRouter image-gen call: model={Model} promptLen={PromptLen} systemPromptLen={SysLen} refImages={ImageCount}",
            _options.Model,
            request.Prompt?.Length ?? 0,
            request.SystemPrompt?.Length ?? 0,
            includedImages);

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }
        messages.Add(new { role = "user", content = userParts });

        var body = new
        {
            model = _options.Model,
            modalities = new[] { "image", "text" },
            messages,
        };

        using var response = await _http.PostAsJsonAsync(
            "chat/completions", body, JsonOptions, cancellationToken).ConfigureAwait(false);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Image gen call failed: HTTP {Status} body={Body}",
                (int)response.StatusCode,
                raw.Length > 600 ? raw[..600] : raw);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException($"OpenRouter image-gen returned error: {err}");
        }

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"Image gen response had no choices. Raw: {Truncate(raw)}");
        }

        var msg = choices[0].GetProperty("message");

        // Newer OpenRouter shape: message.images[*].image_url.url (data URL).
        if (msg.TryGetProperty("images", out var images) &&
            images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
        {
            var firstImage = images[0];
            if (firstImage.TryGetProperty("image_url", out var imgUrl) &&
                imgUrl.TryGetProperty("url", out var urlEl) &&
                urlEl.ValueKind == JsonValueKind.String)
            {
                var url = urlEl.GetString()!;
                return BuildResult(url, root);
            }
        }

        // Older shape: image data URL embedded directly in message.content.
        if (msg.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString() ?? string.Empty;
            if (text.StartsWith("data:image/", StringComparison.Ordinal))
            {
                return BuildResult(text, root);
            }
        }

        throw new InvalidOperationException(
            $"Image gen response had no image in choices[0].message. Raw: {Truncate(raw)}");
    }

    private static ImageGenerationResult BuildResult(string dataUrl, JsonElement root)
    {
        var mime = "image/png";
        if (dataUrl.StartsWith("data:", StringComparison.Ordinal))
        {
            var semi = dataUrl.IndexOf(';');
            if (semi > 5)
            {
                mime = dataUrl.Substring(5, semi - 5);
            }
        }

        int? promptTokens = null;
        int? completionTokens = null;
        decimal? cost = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
                completionTokens = ct.GetInt32();
            if (usage.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number)
                cost = c.GetDecimal();
        }

        return new ImageGenerationResult(dataUrl, mime, promptTokens, completionTokens, cost);
    }

    private static string Truncate(string value)
        => value.Length <= 600 ? value : value[..600];

    private sealed record TextPart(string Text)
    {
        [JsonPropertyName("type")] public string Type => "text";
        [JsonPropertyName("text")] public string Text { get; init; } = Text;
    }

    private sealed record ImagePart(ImageUrl ImageUrl)
    {
        [JsonPropertyName("type")] public string Type => "image_url";
        [JsonPropertyName("image_url")] public ImageUrl ImageUrl { get; init; } = ImageUrl;
    }

    private sealed record ImageUrl([property: JsonPropertyName("url")] string Url);
}
