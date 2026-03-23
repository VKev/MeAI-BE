using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Gemini;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Gemini;

public sealed class GeminiContentModerationService : IGeminiContentModerationService
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-2.0-flash";

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _defaultModel;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GeminiContentModerationService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _apiKey = configuration["Gemini:ApiKey"]
                  ?? throw new InvalidOperationException("Gemini:ApiKey is not configured");
        _baseUrl = configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl;
        _defaultModel = configuration["Gemini:Model"] ?? DefaultModel;
        _httpClient = httpClientFactory.CreateClient("Gemini");
    }

    public async Task<Result<ContentModerationResult>> CheckSensitiveContentAsync(
        ContentModerationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Result.Failure<ContentModerationResult>(
                new Error("ContentModeration.MissingText", "Text content is required for moderation."));
        }

        var prompt = BuildModerationPrompt(request.Text);
        var parts = new List<GeminiPart> { new() { Text = prompt } };

        if (request.MediaResources is { Count: > 0 })
        {
            foreach (var resource in request.MediaResources)
            {
                parts.Add(new GeminiPart
                {
                    FileData = new GeminiFileData
                    {
                        FileUri = resource.FileUri,
                        MimeType = resource.MimeType
                    }
                });
            }
        }

        var payload = new GeminiGenerateContentRequest
        {
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Role = "user",
                    Parts = parts
                }
            }
        };

        var endpoint = BuildGenerateContentEndpoint(request.PreferredModel);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = TryReadErrorMessage(responseBody) ?? "Gemini content moderation request failed.";
                return Result.Failure<ContentModerationResult>(new Error("ContentModeration.RequestFailed", errorMessage));
            }

            var geminiResponse = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, JsonOptions);
            var rawText = geminiResponse?.Candidates?
                .SelectMany(c => c.Content?.Parts ?? Enumerable.Empty<GeminiPart>())
                .Select(p => p.Text)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return Result.Failure<ContentModerationResult>(
                    new Error("ContentModeration.EmptyResponse", "Gemini did not return a moderation result."));
            }

            var moderationResult = ParseModerationResult(rawText);
            if (moderationResult == null)
            {
                return Result.Failure<ContentModerationResult>(
                    new Error("ContentModeration.ParseError", "Could not parse moderation result from Gemini response."));
            }

            return Result.Success(moderationResult);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<ContentModerationResult>(
                new Error("ContentModeration.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<ContentModerationResult>(
                new Error("ContentModeration.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static string BuildModerationPrompt(string text)
    {
        return
            "You are a content moderation AI. Analyze the following social media post text for sensitive content. " +
            "Return ONLY a valid JSON object with no markdown, no explanation, no code fences. " +
            "The JSON must have exactly these fields:\n" +
            "{\n" +
            "  \"is_sensitive\": <true|false>,\n" +
            "  \"category\": <\"violence\"|\"sexual\"|\"hate_speech\"|\"spam\"|\"self_harm\"|null>,\n" +
            "  \"reason\": <string explaining why or null>,\n" +
            "  \"confidence_score\": <float between 0.0 and 1.0>\n" +
            "}\n\n" +
            $"Post content:\n{text}";
    }

    private static ContentModerationResult? ParseModerationResult(string rawText)
    {
        try
        {
            // Strip possible markdown code fences
            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```"))
            {
                var start = cleaned.IndexOf('{');
                var end = cleaned.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    cleaned = cleaned[start..(end + 1)];
                }
            }

            var dto = JsonSerializer.Deserialize<ModerationResultDto>(cleaned, JsonOptions);
            if (dto == null) return null;

            return new ContentModerationResult(
                IsSensitive: dto.IsSensitive,
                Category: string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category,
                Reason: string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason,
                ConfidenceScore: Math.Clamp(dto.ConfidenceScore, 0.0, 1.0));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string BuildGenerateContentEndpoint(string? preferredModel)
    {
        var model = string.IsNullOrWhiteSpace(preferredModel)
            ? _defaultModel
            : preferredModel.Trim();
        return $"{_baseUrl.TrimEnd('/')}/models/{model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";
    }

    private static string? TryReadErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            var error = JsonSerializer.Deserialize<GeminiErrorResponse>(payload, JsonOptions);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Internal DTOs ────────────────────────────────────────────────────────

    private sealed class ModerationResultDto
    {
        [JsonPropertyName("is_sensitive")]
        public bool IsSensitive { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }
    }

    private sealed class GeminiGenerateContentRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = new();
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = new();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("file_data")]
        public GeminiFileData? FileData { get; set; }
    }

    private sealed class GeminiFileData
    {
        [JsonPropertyName("file_uri")]
        public string FileUri { get; set; } = string.Empty;

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "application/octet-stream";
    }

    private sealed class GeminiGenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiErrorResponse
    {
        [JsonPropertyName("error")]
        public GeminiError? Error { get; set; }
    }

    private sealed class GeminiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
