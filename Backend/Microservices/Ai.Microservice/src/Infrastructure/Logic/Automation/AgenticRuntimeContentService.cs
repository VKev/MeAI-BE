using System.Text.Json;
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
    private const string DefaultModel = "gemini-2.0-flash";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Client _client;
    private readonly IConfiguration _configuration;
    private readonly IUserConfigService _userConfigService;
    private readonly ILogger<AgenticRuntimeContentService> _logger;

    public AgenticRuntimeContentService(
        IConfiguration configuration,
        IUserConfigService userConfigService,
        ILogger<AgenticRuntimeContentService> logger)
    {
        _configuration = configuration;
        _userConfigService = userConfigService;
        _logger = logger;

        var apiKey = configuration["Gemini:ApiKey"] ?? configuration["Gemini__ApiKey"];
        _client = string.IsNullOrWhiteSpace(apiKey)
            ? new Client()
            : new Client(apiKey: apiKey);
    }

    public async Task<Result<AgenticRuntimePostDraft>> GeneratePostDraftAsync(
        AgenticRuntimeContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await ResolveModelAsync(cancellationToken);
            var chatClient = _client
                .AsIChatClient(model)
                .AsBuilder()
                .Build();

            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System,
                        """
                        You create concise social media post drafts from verified web search results.
                        Return strict JSON with fields: title, content, hashtag, postType.
                        postType must be "posts".
                        content must be plain text suitable for a social post.
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
                    return Result.Success(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini runtime content generation failed for ScheduleId {ScheduleId}", request.ScheduleId);
        }

        return Result.Success(CreateFallbackDraft(request));
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
            search = request.Search
        }, JsonOptions);

        return $"Create one plain-text social post from this runtime search payload: {payload}";
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

    private sealed class AgenticRuntimePostDraftPayload
    {
        public string? Title { get; set; }

        public string? Content { get; set; }

        public string? Hashtag { get; set; }

        public string? PostType { get; set; }
    }
}
