using System.Text.Json;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Automation;

public sealed class AgenticRuntimeContentService : IAgenticRuntimeContentService
{
    private const string DefaultModel = "gemini-3.1-flash-lite-preview";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfiguration _configuration;
    private readonly IUserConfigService _userConfigService;
    private readonly ILogger<AgenticRuntimeContentService> _logger;
    private readonly IApiCredentialProvider _credentialProvider;

    public AgenticRuntimeContentService(
        IConfiguration configuration,
        IApiCredentialProvider credentialProvider,
        IUserConfigService userConfigService,
        ILogger<AgenticRuntimeContentService> logger)
    {
        _configuration = configuration;
        _credentialProvider = credentialProvider;
        _userConfigService = userConfigService;
        _logger = logger;
    }

    public async Task<Result<AgenticRuntimePostDraft>> GeneratePostDraftAsync(
        AgenticRuntimeContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await ResolveModelAsync(cancellationToken);
            var chatClient = CreateClient()
                .AsIChatClient(model)
                .AsBuilder()
                .Build();

            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System,
                        """
                        You create concise social media post drafts from verified web search results and optional RAG recommendation grounding.
                        Return strict JSON with fields: title, content, hashtag, postType.
                        postType must be "posts".
                        content must be plain text suitable for a social post.
                        Respect maxContentLength as a hard character cap when it is provided.
                        If the payload includes recommendationSummary or recommendationPageProfile, use them to match the account's voice, positioning, and contact details.
                        Keep the post grounded in fresh search results when they are present.
                        Do not wrap the JSON in markdown.
                        """),
                    new ChatMessage(ChatRole.User, BuildPrompt(request))
                ],
                null,
                cancellationToken);

            var raw = response.Messages.LastOrDefault(message => !string.IsNullOrWhiteSpace(message.Text))?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parsed = TryParseDraft(raw);
                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Content))
                {
                    return Result.Success(ApplyContentLimit(parsed, request.MaxContentLength));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini runtime content generation failed for ScheduleId {ScheduleId}", request.ScheduleId);
        }

        return Result.Success(ApplyContentLimit(CreateFallbackDraft(request), request.MaxContentLength));
    }

    private async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var activeConfigResult = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        var configuredModel = _configuration["Gemini:ChatModel"]
                              ?? _configuration["Gemini__ChatModel"]
                              ?? _configuration["Gemini:Model"]
                              ?? _configuration["Gemini__Model"];

        if (activeConfigResult.IsSuccess &&
            !string.IsNullOrWhiteSpace(activeConfigResult.Value?.ChatModel) &&
            activeConfigResult.Value.ChatModel.Trim().StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
        {
            return activeConfigResult.Value.ChatModel.Trim();
        }

        return string.IsNullOrWhiteSpace(configuredModel)
            ? DefaultModel
            : configuredModel.Trim();
    }

    private static string BuildPrompt(AgenticRuntimeContentRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            scheduleId = request.ScheduleId,
            scheduleName = request.ScheduleName,
            agentPrompt = request.AgentPrompt,
            platformPreference = request.PlatformPreference,
            maxContentLength = request.MaxContentLength,
            grounding = new
            {
                socialMediaId = request.GroundingSocialMediaId,
                platform = request.GroundingPlatform,
                recommendationQuery = request.RecommendationQuery,
                recommendationSummary = request.RecommendationSummary,
                recommendationPageProfile = request.RecommendationPageProfile,
                recommendationWebSources = request.RecommendationWebSources,
                ragFallbackReason = request.RagFallbackReason
            },
            search = request.Search
        }, JsonOptions);

        return
            "Create one plain-text social post for immediate scheduled publishing from this payload. " +
            "If recommendationSummary is present, treat it as the primary brand-voice and page-profile grounding. " +
            "Use the web search payload for freshness and facts. If maxContentLength is set, keep content within that hard limit. Return one publishable post only.\n\n" +
            payload;
    }

    private static AgenticRuntimePostDraft? TryParseDraft(string raw)
    {
        var normalized = raw.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            normalized = normalized.Trim('`').Trim();
            if (normalized.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[4..].Trim();
            }
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AgenticRuntimePostDraftPayload>(normalized, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Content))
            {
                return null;
            }

            return new AgenticRuntimePostDraft(
                parsed.Title?.Trim(),
                parsed.Content.Trim(),
                string.IsNullOrWhiteSpace(parsed.Hashtag) ? null : parsed.Hashtag.Trim(),
                string.IsNullOrWhiteSpace(parsed.PostType) ? "posts" : parsed.PostType.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgenticRuntimePostDraft CreateFallbackDraft(AgenticRuntimeContentRequest request)
    {
        var topResult = request.Search.Results.FirstOrDefault();
        var title = request.ScheduleName ?? topResult?.Title ?? "Runtime update";
        var content = string.Join(
            "\n",
            new[]
            {
                request.RecommendationSummary,
                request.AgentPrompt,
                topResult?.Title,
                topResult?.Description,
                topResult?.Url
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new AgenticRuntimePostDraft(
            title,
            string.IsNullOrWhiteSpace(content) ? request.Search.Query : content,
            null,
            "posts");
    }

    private static AgenticRuntimePostDraft ApplyContentLimit(AgenticRuntimePostDraft draft, int? maxContentLength)
    {
        if (!maxContentLength.HasValue || maxContentLength.Value < 1)
        {
            return draft;
        }

        var trimmedContent = TrimToLength(draft.Content, maxContentLength.Value);
        var trimmedTitle = TrimToLength(draft.Title, Math.Min(maxContentLength.Value, 120));
        var trimmedHashtag = TrimToLength(draft.Hashtag, Math.Min(maxContentLength.Value, 200));

        return draft with
        {
            Title = trimmedTitle,
            Content = trimmedContent ?? string.Empty,
            Hashtag = trimmedHashtag
        };
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd();
    }

    private Client CreateClient()
    {
        var apiKey = _credentialProvider.GetOptionalValue("Gemini", "ApiKey");
        return string.IsNullOrWhiteSpace(apiKey)
            ? new Client()
            : new Client(apiKey: apiKey);
    }

    private sealed class AgenticRuntimePostDraftPayload
    {
        public string? Title { get; set; }

        public string? Content { get; set; }

        public string? Hashtag { get; set; }

        public string? PostType { get; set; }
    }
}
