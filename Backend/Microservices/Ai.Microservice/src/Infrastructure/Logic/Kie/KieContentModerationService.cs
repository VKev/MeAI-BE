using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Gemini;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Kie;

public sealed class KieContentModerationService : IGeminiContentModerationService
{
    private readonly string _chatModel;
    private readonly KieResponsesClient _responsesClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public KieContentModerationService(
        IConfiguration configuration,
        KieResponsesClient responsesClient)
    {
        _chatModel = configuration["Kie:ChatModel"] ?? configuration["Kie__ChatModel"] ?? KieResponsesClient.DefaultChatModel;
        _responsesClient = responsesClient;
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

        var contentParts = new List<KieResponsesContentPart>
        {
            new() { Type = "input_text", Text = BuildModerationPrompt(request.Text) }
        };

        if (request.MediaResources is { Count: > 0 })
        {
            foreach (var resource in request.MediaResources)
            {
                contentParts.Add(new KieResponsesContentPart
                {
                    Type = "input_image",
                    ImageUrl = resource.FileUri
                });
            }
        }

        var argumentsResult = await _responsesClient.GetFunctionArgumentsAsync(
            ResolveModel(request.PreferredModel),
            [KieResponsesClient.UserParts(contentParts)],
            BuildModerationTool(),
            "ContentModeration.RequestFailed",
            "Kie content moderation request failed.",
            cancellationToken);

        if (argumentsResult.IsFailure)
        {
            return Result.Failure<ContentModerationResult>(argumentsResult.Error);
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
        return string.IsNullOrWhiteSpace(preferred) ? _chatModel : preferred.Trim();
    }

    private static string BuildModerationPrompt(string text)
    {
        return
            "You are a content moderation AI. Analyze the following social media post text for sensitive content. " +
            "Call the report_sensitive_content tool with your moderation decision. Do not answer in text.\n\n" +
            $"Post content:\n{text}";
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

}
