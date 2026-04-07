using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private readonly string _defaultModel;
    private readonly HttpClient _httpClient;
    private static readonly Regex HashtagSplitRegex = new(@"[\s,]+", RegexOptions.Compiled);
    private static readonly char[] SentenceTrimCharacters = [' ', '.', ',', ';', ':', '!', '?', '-', '"', '\''];
    private static readonly string[] TransientFailureMarkers =
    [
        "high demand",
        "try again later",
        "temporarily unavailable",
        "unavailable",
        "overloaded",
        "quota",
        "rate limit",
        "deadline exceeded",
        "timed out"
    ];
    private static readonly string[] LocalFallbackErrorCodes =
    [
        "Gemini.RequestFailed",
        "Gemini.NetworkError",
        "Gemini.ParseError",
        "Gemini.EmptyResponse"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GeminiCaptionService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _apiKey = configuration["Gemini:ApiKey"]
                  ?? throw new InvalidOperationException("Gemini:ApiKey is not configured");
        _baseUrl = configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl;
        _defaultModel = configuration["Gemini:Model"] ?? DefaultModel;
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
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts = parts
                }
            ]
        };

        var responseTextResult = await SendGenerateContentRequestAsync(
            payload,
            request.PreferredModel,
            "Gemini caption generation failed.",
            "Gemini did not return a caption.",
            cancellationToken);

        if (responseTextResult.IsFailure)
        {
            return Result.Failure<string>(responseTextResult.Error);
        }

        return Result.Success(responseTextResult.Value);
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
                new Error("Gemini.TemplateResourceMissing", "At least one template resource is required."));
        }

        if (request.CaptionCount <= 0)
        {
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                new Error("Gemini.InvalidCaptionCount", "Caption count must be greater than zero."));
        }

        var prompt = BuildSocialMediaPrompt(
            request.Platform,
            request.ResourceHints,
            request.CaptionCount,
            request.LanguageHint,
            request.Instruction);

        var parts = new List<GeminiPart>
        {
            new() { Text = prompt }
        };

        if (hasInlineTemplate)
        {
            parts.Add(new GeminiPart
            {
                InlineData = new GeminiInlineData
                {
                    MimeType = string.IsNullOrWhiteSpace(request.InlineTemplateResource!.MimeType)
                        ? "application/octet-stream"
                        : request.InlineTemplateResource.MimeType.Trim(),
                    Data = Convert.ToBase64String(request.InlineTemplateResource.Content)
                }
            });
        }

        foreach (var resource in request.Resources)
        {
            parts.Add(new GeminiPart
            {
                FileData = new GeminiFileData
                {
                    FileUri = resource.FileUri,
                    MimeType = string.IsNullOrWhiteSpace(resource.MimeType)
                        ? "application/octet-stream"
                        : resource.MimeType.Trim()
                }
            });
        }

        var payload = new GeminiGenerateContentRequest
        {
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts = parts
                }
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseMimeType = "application/json"
            }
        };

        var responseTextResult = await SendGenerateContentRequestAsync(
            payload,
            request.PreferredModel,
            "Gemini caption generation failed.",
            "Gemini did not return caption suggestions.",
            cancellationToken);

        if (responseTextResult.IsFailure)
        {
            if (ShouldUseLocalFallback(responseTextResult.Error))
            {
                return Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(BuildFallbackCaptions(request));
            }

            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(responseTextResult.Error);
        }

        var parsedResult = ParseSocialMediaCaptions(responseTextResult.Value);
        if (parsedResult.IsFailure && ShouldUseLocalFallback(parsedResult.Error))
        {
            return Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(BuildFallbackCaptions(request));
        }

        return parsedResult;
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
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts =
                    [
                        new GeminiPart { Text = prompt }
                    ]
                }
            ]
        };

        var responseTextResult = await SendGenerateContentRequestAsync(
            payload,
            request.PreferredModel,
            "Gemini title generation failed.",
            "Gemini did not return a title.",
            cancellationToken);

        if (responseTextResult.IsFailure)
        {
            if (ShouldUseLocalFallback(responseTextResult.Error))
            {
                return Result.Success(BuildFallbackTitle(request.Content));
            }

            return Result.Failure<string>(responseTextResult.Error);
        }

        return Result.Success(responseTextResult.Value);
    }

    private static bool ShouldUseLocalFallback(Error error)
    {
        if (string.IsNullOrWhiteSpace(error.Code) && string.IsNullOrWhiteSpace(error.Description))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(error.Code) &&
            LocalFallbackErrorCodes.Contains(error.Code, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var haystack = $"{error.Code} {error.Description}".ToLowerInvariant();
        return TransientFailureMarkers.Any(marker => haystack.Contains(marker, StringComparison.Ordinal));
    }

    private static IReadOnlyList<GeminiGeneratedCaption> BuildFallbackCaptions(GeminiSocialMediaCaptionRequest request)
    {
        var platform = request.Platform.ToLowerInvariant();
        var platformName = platform switch
        {
            "facebook" => "Facebook",
            "tiktok" => "TikTok",
            "ig" => "Instagram",
            "threads" => "Threads",
            _ => request.Platform
        };

        var subject = ResolveFallbackSubject(request.ResourceHints, request.Resources);
        var cta = platform switch
        {
            "facebook" => "Send us a message to learn more.",
            "tiktok" => "Follow for the next drop.",
            "ig" => "Save this post for later.",
            "threads" => "Reply with your take.",
            _ => "Check it out today."
        };

        string[] hooks = platform switch
        {
            "facebook" =>
            [
                "A practical update worth sharing",
                "Built for people who want clearer results",
                "A strong option when quality matters"
            ],
            "tiktok" =>
            [
                "POV: you found the upgrade early",
                "This is the kind of drop that stops the scroll",
                "Quick look, strong first impression"
            ],
            "ig" =>
            [
                "A polished look with real everyday value",
                "Clean details, strong finish, easy to love",
                "Designed to stand out without trying too hard"
            ],
            "threads" =>
            [
                "Small detail, big difference",
                "One smart move can change the whole result",
                "This is why thoughtful execution matters"
            ],
            _ =>
            [
                "A fresh update worth noticing",
                "A clear option for your next move",
                "Built to be noticed for the right reasons"
            ]
        };

        var baseHashtags = BuildFallbackHashtags(platform, subject, trending: false);
        var trendingHashtags = BuildFallbackHashtags(platform, subject, trending: true);
        var captions = new List<GeminiGeneratedCaption>(request.CaptionCount);

        for (var index = 0; index < request.CaptionCount; index++)
        {
            var hook = hooks[index % hooks.Length];
            var caption =
                $"{hook}. {subject} is ready for a sharper, more engaging launch on {platformName}. {cta}";

            captions.Add(new GeminiGeneratedCaption(
                caption,
                baseHashtags,
                trendingHashtags,
                cta));
        }

        return captions;
    }

    private static string ResolveFallbackSubject(
        IReadOnlyList<string> resourceHints,
        IReadOnlyList<GeminiCaptionResource> resources)
    {
        var firstHint = resourceHints.FirstOrDefault(hint => !string.IsNullOrWhiteSpace(hint));
        if (!string.IsNullOrWhiteSpace(firstHint))
        {
            return NormalizeSubject(firstHint);
        }

        var mimeType = resources.FirstOrDefault()?.MimeType?.Trim().ToLowerInvariant();
        return mimeType switch
        {
            not null when mimeType.StartsWith("video/", StringComparison.Ordinal) => "This video concept",
            not null when mimeType.StartsWith("image/", StringComparison.Ordinal) => "This visual concept",
            _ => "This campaign concept"
        };
    }

    private static string NormalizeSubject(string hint)
    {
        var cleaned = hint.Trim().Trim(SentenceTrimCharacters);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "This campaign concept";
        }

        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].TrimEnd(SentenceTrimCharacters);
        }

        return char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }

    private static IReadOnlyList<string> BuildFallbackHashtags(
        string platform,
        string subject,
        bool trending)
    {
        var subjectTag = "#" + new string(subject
            .Where(char.IsLetterOrDigit)
            .Take(18)
            .ToArray());

        if (string.IsNullOrWhiteSpace(subjectTag) || subjectTag == "#")
        {
            subjectTag = "#Campaign";
        }

        string[] tags = trending
            ? platform switch
            {
                "facebook" => [subjectTag, "#TrendingNow", "#DiscoverMore", "#WorthSharing"],
                "tiktok" => [subjectTag, "#FYP", "#ForYou", "#TikTokMadeMeWatch"],
                "ig" => [subjectTag, "#ExplorePage", "#InstaDaily", "#NowTrending"],
                "threads" => [subjectTag, "#ThreadsTalk", "#TrendingTopic", "#DailyTake"],
                _ => [subjectTag, "#TrendingNow", "#DiscoverMore", "#DailyUpdate"]
            }
            : platform switch
            {
                "facebook" => [subjectTag, "#FacebookPost", "#BrandStory", "#CommunityUpdate"],
                "tiktok" => [subjectTag, "#TikTok", "#CreatorMode", "#VideoDrop"],
                "ig" => [subjectTag, "#Instagram", "#VisualStory", "#ContentDesign"],
                "threads" => [subjectTag, "#Threads", "#ConversationStarter", "#HotTake"],
                _ => [subjectTag, "#Campaign", "#SocialMedia", "#LaunchReady"]
            };

        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildFallbackTitle(string content)
    {
        var cleaned = content
            .ReplaceLineEndings(" ")
            .Trim();

        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => !word.StartsWith("#", StringComparison.Ordinal))
            .Take(8)
            .ToArray();

        if (words.Length == 0)
        {
            return "Draft Post";
        }

        return string.Join(' ', words).Trim(SentenceTrimCharacters);
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

    private static string BuildSocialMediaPrompt(
        string platform,
        IReadOnlyList<string> resourceList,
        int captionCount,
        string? languageHint,
        string? instruction)
    {
        var platformName = platform switch
        {
            "facebook" => "Facebook",
            "tiktok" => "TikTok",
            "ig" => "Instagram",
            "threads" => "Threads",
            _ => platform
        };

        var languageLine = string.IsNullOrWhiteSpace(languageHint)
            ? "Write the captions in Vietnamese or English."
            : $"Write the captions in {languageHint}.";
        var instructionLine = string.IsNullOrWhiteSpace(instruction)
            ? string.Empty
            : $"Additional instructions: {instruction.Trim()} ";
        var resourceHints = resourceList.Count == 0
            ? "No extra text resource hints were provided."
            : $"Use these resource hints when relevant: {string.Join(", ", resourceList)}.";
        var platformStyle = platform switch
        {
            "facebook" => "Use a community-oriented, informative tone with a clear value proposition.",
            "tiktok" => "Use a fast hook, energetic tone, and creator-style phrasing.",
            "ig" => "Use a visually descriptive, polished tone that fits Instagram feed copy.",
            "threads" => "Use a conversational, opinionated tone that feels native to Threads.",
            _ => string.Empty
        };

        return
            $"You are a senior social media copywriter. Analyze the attached template resource and create {captionCount} distinct {platformName} caption options. " +
            "Return valid JSON only. Do not use markdown, code fences, headings, or commentary. " +
            "Use exactly this shape: " +
            "{\"captions\":[{\"caption\":\"string\",\"hashtags\":[\"#tag\"],\"trendingHashtags\":[\"#trend\"],\"callToAction\":\"string\"}]}. " +
            $"{languageLine} " +
            $"{platformStyle} " +
            $"{resourceHints} " +
            instructionLine +
            "Each caption must feel platform-native, concise, and engaging. " +
            "Each caption must include 3 to 6 relevant hashtags and 3 to 5 trending-style hashtags. " +
            "CallToAction should be short and actionable.";
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

    private string BuildGenerateContentEndpoint(string? preferredModel)
    {
        var model = string.IsNullOrWhiteSpace(preferredModel)
            ? _defaultModel
            : preferredModel.Trim();

        return $"{_baseUrl.TrimEnd('/')}/models/{model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";
    }

    private async Task<Result<string>> SendGenerateContentRequestAsync(
        GeminiGenerateContentRequest payload,
        string? preferredModel,
        string requestFailureMessage,
        string emptyResponseMessage,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildGenerateContentEndpoint(preferredModel);
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
                var errorMessage = TryReadErrorMessage(responseBody) ?? requestFailureMessage;
                return Result.Failure<string>(new Error("Gemini.RequestFailed", errorMessage));
            }

            var result = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseBody, JsonOptions);
            var text = result?.Candidates?
                .SelectMany(candidate => candidate.Content?.Parts ?? Enumerable.Empty<GeminiPart>())
                .Select(part => part.Text)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (string.IsNullOrWhiteSpace(text))
            {
                return Result.Failure<string>(
                    new Error("Gemini.EmptyResponse", emptyResponseMessage));
            }

            return Result.Success(text.Trim());
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

    private static Result<IReadOnlyList<GeminiGeneratedCaption>> ParseSocialMediaCaptions(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                new Error("Gemini.EmptyResponse", "Gemini did not return caption suggestions."));
        }

        var normalizedPayload = StripCodeFence(payload);

        try
        {
            using var document = JsonDocument.Parse(normalizedPayload);
            JsonElement captionsElement;

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                captionsElement = document.RootElement;
            }
            else if (TryGetProperty(document.RootElement, "captions", out var nestedCaptions) &&
                     nestedCaptions.ValueKind == JsonValueKind.Array)
            {
                captionsElement = nestedCaptions;
            }
            else
            {
                return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                    new Error("Gemini.ParseError", "Gemini response did not contain a captions array."));
            }

            var captions = new List<GeminiGeneratedCaption>();

            foreach (var item in captionsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var caption = GetStringProperty(item, "caption") ??
                              GetStringProperty(item, "text") ??
                              GetStringProperty(item, "content");

                if (string.IsNullOrWhiteSpace(caption))
                {
                    continue;
                }

                var hashtags = ReadHashtagList(item, "hashtags");
                var trendingHashtags = ReadHashtagList(item, "trendingHashtags", "trending_hashtags");
                var callToAction = GetStringProperty(item, "callToAction") ??
                                   GetStringProperty(item, "cta");

                captions.Add(new GeminiGeneratedCaption(
                    caption.Trim(),
                    hashtags,
                    trendingHashtags,
                    string.IsNullOrWhiteSpace(callToAction) ? null : callToAction.Trim()));
            }

            if (captions.Count == 0)
            {
                return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                    new Error("Gemini.ParseError", "Gemini response did not contain any usable captions."));
            }

            return Result.Success<IReadOnlyList<GeminiGeneratedCaption>>(captions);
        }
        catch (JsonException ex)
        {
            return Result.Failure<IReadOnlyList<GeminiGeneratedCaption>>(
                new Error("Gemini.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static string StripCodeFence(string payload)
    {
        var trimmed = payload.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine >= 0)
        {
            trimmed = trimmed[(firstNewLine + 1)..];
        }

        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }

    private static IReadOnlyList<string> ReadHashtagList(
        JsonElement item,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var value))
            {
                continue;
            }

            return NormalizeHashtags(value);
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> NormalizeHashtags(JsonElement value)
    {
        var hashtags = value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToList(),
            JsonValueKind.String => HashtagSplitRegex
                .Split(value.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList(),
            _ => new List<string>()
        };

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();

        foreach (var hashtag in hashtags)
        {
            var cleaned = hashtag?.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (!cleaned.StartsWith("#", StringComparison.Ordinal))
            {
                cleaned = $"#{cleaned.TrimStart('#')}";
            }

            if (unique.Add(cleaned))
            {
                normalized.Add(cleaned);
            }
        }

        return normalized;
    }

    private static string? GetStringProperty(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(item, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (NormalizePropertyName(property.Name) == NormalizePropertyName(propertyName))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        var buffer = new StringBuilder(propertyName.Length);

        foreach (var character in propertyName)
        {
            if (character is ' ' or '_' or '-')
            {
                continue;
            }

            buffer.Append(char.ToLowerInvariant(character));
        }

        return buffer.ToString();
    }

    private sealed class GeminiGenerateContentRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = new();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
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

        [JsonPropertyName("inline_data")]
        public GeminiInlineData? InlineData { get; set; }
    }

    private sealed class GeminiFileData
    {
        [JsonPropertyName("file_uri")]
        public string FileUri { get; set; } = string.Empty;

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "application/octet-stream";
    }

    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "application/octet-stream";

        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("response_mime_type")]
        public string? ResponseMimeType { get; set; }
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
