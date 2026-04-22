using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Abstractions.Gemini;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Kie;

// Caption generator backed by Kie's GPT-5.4 Responses API (POST /codex/v1/responses).
// Implements IGeminiCaptionService so all existing Gemini* command/request records and
// the GenerateSocialMediaCaptionsCommand handler keep working unchanged — only the DI
// binding + the HTTP client behind the interface change. Replaces the old Mistral
// integration which was getting rate-limited on the free tier.
public sealed class KieCaptionService : IGeminiCaptionService
{
    private const string DefaultBaseUrl = "https://api.kie.ai";
    private const string DefaultChatModel = "gpt-5-4";
    private const string ResponsesPath = "/codex/v1/responses";

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _chatModel;
    private readonly HttpClient _httpClient;
    private readonly ILogger<KieCaptionService> _logger;

    private static readonly Regex HashtagSplitRegex = new(@"[\s,]+", RegexOptions.Compiled);
    private static readonly char[] SentenceTrimCharacters = [' ', '.', ',', ';', ':', '!', '?', '-', '"', '\''];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public KieCaptionService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<KieCaptionService> logger)
    {
        _apiKey = configuration["Kie:ApiKey"]
                  ?? configuration["Kie__ApiKey"]
                  ?? throw new InvalidOperationException("Kie:ApiKey is not configured");
        _baseUrl = (configuration["Kie:BaseUrl"] ?? configuration["Kie__BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');
        _chatModel = configuration["Kie:ChatModel"] ?? configuration["Kie__ChatModel"] ?? DefaultChatModel;
        _httpClient = httpClientFactory.CreateClient("KieChat");
        _logger = logger;
    }

    public async Task<Result<string>> GenerateCaptionAsync(
        GeminiCaptionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Resources.Count == 0)
        {
            return Result.Failure<string>(
                new Error("Kie.MissingResources", "At least one resource is required for caption generation."));
        }

        var prompt = BuildPrompt(request.PostType, request.LanguageHint, request.Instruction);
        var input = new List<ResponsesInputItem>
        {
            BuildUserInput(prompt, request.Resources.Select(r => r.FileUri))
        };

        var responseResult = await SendResponsesAsync(
            new ResponsesRequest
            {
                Model = ResolveModel(request.PreferredModel),
                Stream = false,
                Input = input
            },
            "Kie caption generation failed.",
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return Result.Failure<string>(responseResult.Error);
        }

        return Result.Success(responseResult.Value);
    }

    public async Task<Result<IReadOnlyList<GeminiGeneratedCaption>>> GenerateSocialMediaCaptionsAsync(
        GeminiSocialMediaCaptionRequest request,
        CancellationToken cancellationToken)
    {
        var hasInlineTemplate = request.InlineTemplateResource is { Content.Length: > 0 };
        var hasResources = request.Resources.Count > 0;

        if (!hasInlineTemplate && !hasResources)
        {
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                new Error("Kie.TemplateResourceMissing", "At least one template resource is required."));
        }

        if (request.CaptionCount <= 0)
        {
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                new Error("Kie.InvalidCaptionCount", "Caption count must be greater than zero."));
        }

        var prompt = BuildSocialMediaPrompt(
            request.Platform,
            request.PostType,
            request.ResourceHints,
            request.CaptionCount,
            request.LanguageHint,
            request.Instruction,
            hasImages: hasInlineTemplate || hasResources);

        var contentParts = new List<ResponsesContentPart>
        {
            new() { Type = "input_text", Text = prompt }
        };

        if (hasInlineTemplate)
        {
            var mime = string.IsNullOrWhiteSpace(request.InlineTemplateResource!.MimeType)
                ? "application/octet-stream"
                : request.InlineTemplateResource.MimeType.Trim();
            var base64 = Convert.ToBase64String(request.InlineTemplateResource.Content);
            contentParts.Add(new ResponsesContentPart
            {
                Type = "input_image",
                ImageUrl = $"data:{mime};base64,{base64}"
            });
        }

        foreach (var resource in request.Resources)
        {
            // GPT-5.4 Responses API accepts `input_image.image_url` as a plain STRING
            // (not an object), unlike Chat Completions. Presigned S3 URLs work as-is.
            contentParts.Add(new ResponsesContentPart
            {
                Type = "input_image",
                ImageUrl = resource.FileUri
            });
        }

        var input = new List<ResponsesInputItem>
        {
            new() { Role = "user", Content = contentParts }
        };

        var responseResult = await SendResponsesAsync(
            new ResponsesRequest
            {
                Model = ResolveModel(request.PreferredModel),
                Stream = false,
                Input = input
            },
            "Kie caption generation failed.",
            cancellationToken);

        if (responseResult.IsFailure)
        {
            if (ShouldUseLocalFallback(responseResult.Error))
            {
                return Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(BuildFallbackCaptions(request));
            }
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(responseResult.Error);
        }

        var parsed = ParseSocialMediaCaptions(responseResult.Value, request.CaptionCount);
        if (parsed.IsFailure && ShouldUseLocalFallback(parsed.Error))
        {
            return Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(BuildFallbackCaptions(request));
        }
        return parsed;
    }

    public async Task<Result<string>> GenerateTitleAsync(
        GeminiTitleRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Result.Failure<string>(
                new Error("Kie.MissingContent", "Content is required for title generation."));
        }

        var prompt = BuildTitlePrompt(request.Content, request.LanguageHint);
        var input = new List<ResponsesInputItem>
        {
            new()
            {
                Role = "user",
                Content = new List<ResponsesContentPart>
                {
                    new() { Type = "input_text", Text = prompt }
                }
            }
        };

        var responseResult = await SendResponsesAsync(
            new ResponsesRequest
            {
                Model = ResolveModel(request.PreferredModel),
                Stream = false,
                Input = input
            },
            "Kie title generation failed.",
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return Result.Failure<string>(responseResult.Error);
        }

        return Result.Success(responseResult.Value.Trim(SentenceTrimCharacters));
    }

    private string ResolveModel(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred.Trim();
        return _chatModel;
    }

    private async Task<Result<string>> SendResponsesAsync(
        ResponsesRequest payload,
        string defaultFailureMessage,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{ResponsesPath}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kie request failed.");
            return Result.Failure<string>(new Error("Kie.NetworkError", ex.Message));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Kie.RequestFailed", $"{defaultFailureMessage} Status={(int)response.StatusCode}: {body}"));
        }

        try
        {
            var text = ExtractOutputText(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Result.Failure<string>(new Error("Kie.EmptyResponse", "Kie returned an empty response."));
            }
            return Result.Success(text.Trim());
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(new Error("Kie.ParseError", ex.Message));
        }
    }

    // Walk the Responses-API output array and concatenate every `output_text` fragment.
    // Shape: { output: [ {type:"message", content:[{type:"output_text", text:"..."}]}, ... ] }
    // Skips `reasoning` items (they exist but have empty user-visible content).
    private static string ExtractOutputText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProp.GetString();
                if (type is not ("output_text" or "text"))
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    buffer.Append(textProp.GetString());
                }
            }
        }

        return buffer.ToString();
    }

    private static ResponsesInputItem BuildUserInput(string prompt, IEnumerable<string> imageUrls)
    {
        var parts = new List<ResponsesContentPart> { new() { Type = "input_text", Text = prompt } };
        foreach (var url in imageUrls)
        {
            parts.Add(new ResponsesContentPart { Type = "input_image", ImageUrl = url });
        }
        return new ResponsesInputItem { Role = "user", Content = parts };
    }

    private static string BuildPrompt(string postType, string? languageHint, string? instruction)
    {
        var language = string.IsNullOrWhiteSpace(languageHint) ? "English" : languageHint.Trim();
        var builder = new StringBuilder();
        builder.AppendLine($"Write a single compelling social-media caption for a {postType} post.");
        builder.AppendLine($"Language: {language}.");
        builder.AppendLine("Keep it under 220 characters. No markdown.");
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.AppendLine($"Additional instruction: {instruction.Trim()}.");
        }
        return builder.ToString();
    }

    private static string BuildSocialMediaPrompt(
        string platform,
        string? postType,
        IReadOnlyList<string> resourceHints,
        int captionCount,
        string? languageHint,
        string? instruction,
        bool hasImages)
    {
        var language = string.IsNullOrWhiteSpace(languageHint) ? "English" : languageHint.Trim();
        var platformName = NormalizePlatformLabel(platform);
        var postTypeLabel = NormalizePostTypeLabel(postType, platform);
        var toneGuidance = BuildToneGuidance(platformName, postTypeLabel);

        var builder = new StringBuilder();
        if (hasImages)
        {
            // Explicitly tell GPT-5.4 to actually LOOK at the attached images — otherwise
            // it sometimes hallucinates generic copy without referencing visible content.
            builder.AppendLine(
                "You are writing social-media captions for the images attached in this message.");
            builder.AppendLine(
                "Before writing, examine every image carefully: subject, setting, colors, activity, mood, text-on-image.");
            builder.AppendLine(
                "Ground each caption in what's actually visible — don't invent details that aren't in the images.");
        }
        else
        {
            builder.AppendLine("You are writing social-media captions. No reference media is attached — rely on the hints below.");
        }

        builder.AppendLine();
        builder.AppendLine($"Target platform: {platformName}");
        builder.AppendLine($"Post format: {postTypeLabel}");
        builder.AppendLine($"Language: {language}");
        builder.AppendLine($"Number of captions to produce: {captionCount} (distinct, different hooks/angles each).");
        builder.AppendLine();
        builder.AppendLine(toneGuidance);

        if (resourceHints.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Context hints from the user's earlier draft (use for tone/topic, don't copy verbatim):");
            foreach (var hint in resourceHints)
            {
                builder.AppendLine($"- {hint}");
            }
        }
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.AppendLine();
            builder.AppendLine($"Additional creator instruction: {instruction.Trim()}");
        }

        builder.AppendLine();
        builder.AppendLine("Return ONLY a JSON object with this exact shape (no prose, no markdown fences, no commentary outside the JSON):");
        builder.AppendLine(
            "{ \"captions\": [ { \"caption\": string, \"hashtags\": [string], \"trendingHashtags\": [string], \"callToAction\": string } ] }");
        return builder.ToString();
    }

    private static string NormalizePostTypeLabel(string? postType, string platform)
    {
        var normalized = (postType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "reel" or "reels")
        {
            return platform.Equals("tiktok", StringComparison.OrdinalIgnoreCase)
                ? "short-form video (TikTok style)"
                : "reel (short-form vertical video)";
        }
        if (normalized is "video")
        {
            return "short-form video";
        }
        return platform.Equals("tiktok", StringComparison.OrdinalIgnoreCase)
            ? "short-form video (TikTok style)"
            : "feed post";
    }

    private static string BuildToneGuidance(string platformName, string postTypeLabel)
    {
        var isReelLike = postTypeLabel.Contains("video", StringComparison.OrdinalIgnoreCase)
                        || postTypeLabel.Contains("reel", StringComparison.OrdinalIgnoreCase);

        if (isReelLike)
        {
            return
                $"Tone guidance: {platformName} short-form video captions should lead with a 1-line hook (<= 10 words) " +
                "that stops scroll, use casual conversational language, and close with an invitation to watch/save/share. " +
                "Keep total length under 150 characters. 3-5 hashtags max, mix niche + trending.";
        }

        return platformName switch
        {
            "Instagram" =>
                "Tone guidance: Instagram feed posts can run 150-220 characters — warm, visual, emoji-friendly. " +
                "Start with a strong first line (the part that shows before 'more'). Include 5-10 relevant hashtags.",
            "Facebook" =>
                "Tone guidance: Facebook feed posts can be 150-300 characters — conversational, slightly longer, " +
                "share-friendly. Avoid heavy hashtag stacking (2-4 is plenty).",
            "Threads" =>
                "Tone guidance: Threads is short + text-first (<= 500 chars). Punchy, meme-aware, 0-3 hashtags.",
            _ =>
                "Tone guidance: match platform best practices, keep captions concise and scroll-stopping."
        };
    }

    private static string BuildTitlePrompt(string content, string? languageHint)
    {
        var language = string.IsNullOrWhiteSpace(languageHint) ? "English" : languageHint.Trim();
        return $"Summarize the following content into a punchy title in {language} (max 12 words, no punctuation at the end).\n\n{content}";
    }

    private static string NormalizePlatformLabel(string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            "facebook" => "Facebook",
            "tiktok" => "TikTok",
            "ig" or "instagram" => "Instagram",
            "threads" => "Threads",
            _ => platform
        };
    }

    private static Result<IReadOnlyList<GeminiGeneratedCaption>> ParseSocialMediaCaptions(string text, int expected)
    {
        // GPT-5.4 usually obeys the JSON-only instruction but occasionally wraps it in
        // fences — strip markdown fences defensively before parsing.
        var trimmed = StripMarkdownFence(text);

        try
        {
            var parsed = JsonSerializer.Deserialize<CaptionPayload>(trimmed, JsonOptions);
            if (parsed?.Captions is null || parsed.Captions.Count == 0)
            {
                return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                    new Error("Kie.EmptyResponse", "Kie did not return any captions."));
            }

            var list = parsed.Captions
                .Take(expected)
                .Select(c => new GeminiGeneratedCaption(
                    c.Caption?.Trim() ?? string.Empty,
                    NormalizeTagList(c.Hashtags),
                    NormalizeTagList(c.TrendingHashtags),
                    string.IsNullOrWhiteSpace(c.CallToAction) ? null : c.CallToAction.Trim()))
                .Where(c => !string.IsNullOrWhiteSpace(c.Caption))
                .ToList();

            if (list.Count == 0)
            {
                return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                    new Error("Kie.EmptyResponse", "Kie captions contained no usable content."));
            }

            return Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(list);
        }
        catch (JsonException ex)
        {
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                new Error("Kie.ParseError", ex.Message));
        }
    }

    private static IReadOnlyList<string> NormalizeTagList(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0) return Array.Empty<string>();
        return tags
            .SelectMany(t => HashtagSplitRegex.Split(t ?? string.Empty))
            .Select(t => t.TrimStart('#').Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string StripMarkdownFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }
        }
        return trimmed.Trim();
    }

    private static bool ShouldUseLocalFallback(Error error)
    {
        if (string.IsNullOrWhiteSpace(error.Code) && string.IsNullOrWhiteSpace(error.Description)) return false;
        var haystack = $"{error.Code} {error.Description}".ToLowerInvariant();
        return haystack.Contains("networkerror") ||
               haystack.Contains("emptyresponse") ||
               haystack.Contains("parseerror") ||
               haystack.Contains("timeout") ||
               haystack.Contains("rate limit") ||
               haystack.Contains("quota");
    }

    private static IReadOnlyList<GeminiGeneratedCaption> BuildFallbackCaptions(GeminiSocialMediaCaptionRequest request)
    {
        var platform = NormalizePlatformLabel(request.Platform);
        var subject = request.ResourceHints.FirstOrDefault() ?? "your latest post";
        var trimmed = subject.Length > 60 ? subject[..60].TrimEnd() : subject;

        return Enumerable.Range(1, request.CaptionCount)
            .Select(i => new GeminiGeneratedCaption(
                $"{trimmed} ({platform} idea #{i}).",
                new[] { "creator", "content" },
                Array.Empty<string>(),
                "Tap to learn more"))
            .ToList();
    }

    // --- DTOs for the Kie Responses API ---

    private sealed class ResponsesRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = default!;
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("input")] public IReadOnlyList<ResponsesInputItem> Input { get; set; } = Array.Empty<ResponsesInputItem>();
    }

    private sealed class ResponsesInputItem
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public IReadOnlyList<ResponsesContentPart> Content { get; set; } = Array.Empty<ResponsesContentPart>();
    }

    private sealed class ResponsesContentPart
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "input_text";
        [JsonPropertyName("text")] public string? Text { get; set; }
        // GPT-5.4 expects `image_url` as a plain string, not an object. Serializer emits
        // it as `"image_url": "https://..."` when set, or omits it when null.
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    }

    private sealed class CaptionPayload
    {
        [JsonPropertyName("captions")] public List<CaptionDraft>? Captions { get; set; }
    }

    private sealed class CaptionDraft
    {
        [JsonPropertyName("caption")] public string? Caption { get; set; }
        [JsonPropertyName("hashtags")] public List<string>? Hashtags { get; set; }
        [JsonPropertyName("trendingHashtags")] public List<string>? TrendingHashtags { get; set; }
        [JsonPropertyName("callToAction")] public string? CallToAction { get; set; }
    }
}
