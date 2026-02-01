using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Gemini;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Gemini;

public sealed class GeminiCaptionService : IGeminiCaptionService
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-3-flash-preview";

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiCaptionService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _apiKey = configuration["Gemini:ApiKey"]
                  ?? throw new InvalidOperationException("Gemini:ApiKey is not configured");
        _baseUrl = configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl;
        _model = configuration["Gemini:Model"] ?? DefaultModel;
        _httpClient = httpClientFactory.CreateClient("Gemini");
    }

    public async Task<Result<string>> GenerateCaptionAsync(
        GeminiCaptionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Resources.Count == 0)
        {
            return Result.Failure<string>(
                new Error("Gemini.MissingResources", "At least one resource is required for caption generation."));
        }

        var prompt = BuildPrompt(request.PostType, request.LanguageHint, request.Instruction);
        var parts = new List<GeminiPart>
        {
            new() { Text = prompt }
        };

        foreach (var resource in request.Resources)
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

        var endpoint = $"{_baseUrl.TrimEnd('/')}/models/{_model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";
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
                var errorMessage = TryReadErrorMessage(responseBody) ?? "Gemini caption generation failed.";
                return Result.Failure<string>(new Error("Gemini.RequestFailed", errorMessage));
            }

            var result = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, JsonOptions);
            var caption = result?.Candidates?
                .SelectMany(candidate => candidate.Content?.Parts ?? Enumerable.Empty<GeminiPart>())
                .Select(part => part.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            if (string.IsNullOrWhiteSpace(caption))
            {
                return Result.Failure<string>(
                    new Error("Gemini.EmptyResponse", "Gemini did not return a caption."));
            }

            return Result.Success(caption.Trim());
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(
                new Error("Gemini.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(
                new Error("Gemini.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<string>> GenerateTitleAsync(
        GeminiTitleRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Result.Failure<string>(
                new Error("Gemini.MissingContent", "Content is required for title generation."));
        }

        var prompt = BuildTitlePrompt(request.Content, request.LanguageHint);
        var payload = new GeminiGenerateContentRequest
        {
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Role = "user",
                    Parts = new List<GeminiPart>
                    {
                        new() { Text = prompt }
                    }
                }
            }
        };

        var endpoint = $"{_baseUrl.TrimEnd('/')}/models/{_model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";
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
                var errorMessage = TryReadErrorMessage(responseBody) ?? "Gemini title generation failed.";
                return Result.Failure<string>(new Error("Gemini.RequestFailed", errorMessage));
            }

            var result = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, JsonOptions);
            var title = result?.Candidates?
                .SelectMany(candidate => candidate.Content?.Parts ?? Enumerable.Empty<GeminiPart>())
                .Select(part => part.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            if (string.IsNullOrWhiteSpace(title))
            {
                return Result.Failure<string>(
                    new Error("Gemini.EmptyResponse", "Gemini did not return a title."));
            }

            return Result.Success(title.Trim());
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(
                new Error("Gemini.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(
                new Error("Gemini.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static string BuildPrompt(string postType, string? languageHint, string? instruction)
    {
        var normalizedPostType = string.IsNullOrWhiteSpace(postType) ? "posts" : postType.Trim();
        var languageLine = string.IsNullOrWhiteSpace(languageHint)
            ? "Write the caption in Vietnamese or English."
            : $"Write the caption in {languageHint}.";
        var instructionLine = string.IsNullOrWhiteSpace(instruction)
            ? string.Empty
            : $"Additional instructions: {instruction.Trim()} ";

        return $"Create a single Facebook {normalizedPostType} caption based on the attached media. " +
               "Return exactly one caption only (no options, no headings, no prefacing). " +
               $"{languageLine} " +
               instructionLine +
               "Use a friendly tone, include relevant hashtags and a couple of emojis. " +
               "Keep it concise and engaging. Avoid markdown formatting.";
    }

    private static string BuildTitlePrompt(string content, string? languageHint)
    {
        var languageLine = string.IsNullOrWhiteSpace(languageHint)
            ? "Write the title in Vietnamese or English."
            : $"Write the title in {languageHint}.";

        return "Summarize the following content into a short title (max 8 words). " +
               "Return only the title, no quotes, no hashtags, no extra text. " +
               $"{languageLine}\n\nContent:\n{content}";
    }

    private static string? TryReadErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

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
