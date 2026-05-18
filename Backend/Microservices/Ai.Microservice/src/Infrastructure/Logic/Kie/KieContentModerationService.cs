using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Gemini;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Kie;

public sealed class KieContentModerationService : IGeminiContentModerationService
{
    private readonly string _chatModel;
    private readonly KieResponsesClient _responsesClient;
    private readonly ILogger<KieContentModerationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public KieContentModerationService(
        IConfiguration configuration,
        KieResponsesClient responsesClient,
        ILogger<KieContentModerationService> logger)
    {
        _chatModel = configuration["Kie:ChatModel"] ?? configuration["Kie__ChatModel"] ?? KieResponsesClient.DefaultChatModel;
        _responsesClient = responsesClient;
        _logger = logger;
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

        var model = ResolveModel(request.PreferredModel);
        _logger.LogInformation(
            "Calling Kie content moderation. Model={Model} HasMedia={HasMedia} TextPreview={TextPreview}",
            model,
            request.MediaResources is { Count: > 0 },
            Preview(request.Text));

        var argumentsResult = await _responsesClient.GetFunctionArgumentsAsync(
            model,
            BuildModerationFunctionInput(request),
            BuildModerationTool(),
            "ContentModeration.RequestFailed",
            "Kie content moderation request failed.",
            cancellationToken);

        if (argumentsResult.IsFailure)
        {
            _logger.LogWarning(
                "Kie content moderation function-call mode failed. Model={Model} ErrorCode={ErrorCode} ErrorDescription={ErrorDescription}. Falling back to JSON-only mode.",
                model,
                argumentsResult.Error.Code,
                argumentsResult.Error.Description);

            argumentsResult = await _responsesClient.GetTextResponseAsync(
                model,
                BuildModerationJsonFallbackInput(request),
                "ContentModeration.RequestFailed",
                "Kie content moderation request failed.",
                cancellationToken);

            if (argumentsResult.IsFailure)
            {
                _logger.LogWarning(
                    "Kie content moderation JSON-only fallback also failed. Model={Model} ErrorCode={ErrorCode} ErrorDescription={ErrorDescription}",
                    model,
                    argumentsResult.Error.Code,
                    argumentsResult.Error.Description);

                return Result.Failure<ContentModerationResult>(argumentsResult.Error);
            }
        }

        var moderationResult = ParseModerationResult(argumentsResult.Value);
        if (moderationResult is null)
        {
            return Result.Failure<ContentModerationResult>(
                new Error("ContentModeration.ParseError", "Could not parse moderation result from Kie response."));
        }

        return Result.Success(moderationResult);
    }

    private string ResolveModel(string? preferred)
    {
        return KieResponsesClient.ResolveResponsesModel(preferred, _chatModel);
    }

    private static string BuildModerationPrompt(string text)
    {
        return
            "You are a content moderation AI. Analyze the following social media post text for sensitive content. " +
            "Call the report_sensitive_content tool with your moderation decision. Do not answer in text.\n\n" +
            $"Post content:\n{text}";
    }

    private static string BuildModerationJsonFallbackPrompt()
    {
        return
            """
            You are a content moderation AI.

            Task:
            - Analyze the provided social media post text and any attached images for sensitive content.
            - Return JSON only.
            - Do not use markdown fences.
            - Do not add explanation text outside the JSON.

            Required JSON object shape:
            {
              "is_sensitive": true | false,
              "category": "violence | sexual | hate_speech | spam | self_harm | null",
              "reason": "string or null",
              "confidence_score": 0.0
            }
            """;
    }

    private static IReadOnlyList<KieResponsesInputItem> BuildModerationFunctionInput(ContentModerationRequest request)
    {
        return
        [
            KieResponsesClient.UserParts(BuildModerationUserParts(
                BuildModerationPrompt(request.Text),
                request.MediaResources))
        ];
    }

    private static IReadOnlyList<KieResponsesInputItem> BuildModerationJsonFallbackInput(ContentModerationRequest request)
    {
        return
        [
            KieResponsesClient.DeveloperText(BuildModerationJsonFallbackPrompt()),
            KieResponsesClient.UserParts(BuildModerationUserParts(
                $"Post content:\n{request.Text}",
                request.MediaResources))
        ];
    }

    private static List<KieResponsesContentPart> BuildModerationUserParts(
        string text,
        IReadOnlyList<ContentModerationResource>? mediaResources)
    {
        var contentParts = new List<KieResponsesContentPart>
        {
            new() { Type = "input_text", Text = text }
        };

        if (mediaResources is not { Count: > 0 })
        {
            return contentParts;
        }

        foreach (var resource in mediaResources)
        {
            contentParts.Add(new KieResponsesContentPart
            {
                Type = "input_image",
                ImageUrl = resource.FileUri
            });
        }

        return contentParts;
    }

    private static KieResponsesFunctionTool BuildModerationTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "report_sensitive_content",
            Description = "Report whether a social media post contains sensitive content.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "is_sensitive", "category", "reason", "confidence_score" },
                properties = new
                {
                    is_sensitive = new
                    {
                        type = "boolean",
                        description = "True when the content is sensitive or unsafe for normal publishing."
                    },
                    category = new
                    {
                        type = new[] { "string", "null" },
                        @enum = new object?[] { "violence", "sexual", "hate_speech", "spam", "self_harm", null },
                        description = "The best matching sensitive-content category, or null when not sensitive."
                    },
                    reason = new
                    {
                        type = new[] { "string", "null" },
                        description = "Short explanation for the moderation result."
                    },
                    confidence_score = new
                    {
                        type = "number",
                        minimum = 0,
                        maximum = 1,
                        description = "Confidence score from 0.0 to 1.0."
                    }
                }
            }
        };
    }

    private static ContentModerationResult? ParseModerationResult(string rawText)
    {
        try
        {
            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                var start = cleaned.IndexOf('{');
                var end = cleaned.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    cleaned = cleaned[start..(end + 1)];
                }
            }

            var dto = JsonSerializer.Deserialize<ModerationResultDto>(cleaned, JsonOptions);
            if (dto is null)
            {
                return null;
            }

            return new ContentModerationResult(
                dto.IsSensitive,
                string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category,
                string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason,
                Math.Clamp(dto.ConfidenceScore, 0.0, 1.0));
        }
        catch (JsonException)
        {
            return null;
        }
    }

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

    private static string Preview(string? value, int maxLength = 400)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var normalized = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...(truncated,total={normalized.Length})";
    }

}
