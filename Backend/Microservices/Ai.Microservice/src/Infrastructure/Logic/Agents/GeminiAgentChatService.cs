using System.Text.Json;
using Application.Abstractions.Agents;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Configs;
using Application.Agents;
using Application.Agents.Models;
using Application.Chats.Commands;
using Application.Posts.Commands;
using Domain.Entities;
using Google.GenAI;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.ImageGenerating;

namespace Infrastructure.Logic.Agents;

public sealed class GeminiAgentChatService : IAgentChatService
{
    private const string DefaultModel = "gemini-3.1-flash-lite-preview";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly IUserConfigService _userConfigService;
    private readonly IMediator _mediator;
    private readonly ILogger<GeminiAgentChatService> _logger;
    private readonly IApiCredentialProvider _credentialProvider;
    private readonly IChatWebPostService _chatWebPostService;

    public GeminiAgentChatService(
        IConfiguration configuration,
        IApiCredentialProvider credentialProvider,
        IUserConfigService userConfigService,
        IMediator mediator,
        IChatWebPostService chatWebPostService,
        ILogger<GeminiAgentChatService> logger)
    {
        _configuration = configuration;
        _credentialProvider = credentialProvider;
        _userConfigService = userConfigService;
        _mediator = mediator;
        _chatWebPostService = chatWebPostService;
        _logger = logger;
    }

    public async Task<Result<AgentChatCompletionResult>> GenerateReplyAsync(
        AgentChatRequest request,
        CancellationToken cancellationToken)
    {
        var obviousValidation = TryBuildObviousValidation(request.Message);
        if (obviousValidation is not null)
        {
            return Result.Success(obviousValidation);
        }

        var model = await ResolveModelAsync(cancellationToken);
        var analysisResult = await AnalyzePromptAsync(request.Message, model, cancellationToken);
        if (analysisResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(analysisResult.Error);
        }

        var analysis = analysisResult.Value;
        return analysis.Action switch
        {
            AgentActions.ValidationFailed => Result.Success(new AgentChatCompletionResult(
                analysis.AssistantMessage ?? "Yeu cau chua du ro de tao noi dung.",
                model,
                [],
                [],
                analysis.Action,
                analysis.ValidationError,
                analysis.RevisedPrompt)),

            AgentActions.ImageCreated => await CreateImageAndPostAsync(request, model, analysis, cancellationToken),

            AgentActions.PostCreated => await CreateDraftPostAsync(request, model, analysis, cancellationToken),

            AgentActions.WebPostCreated => await CreateWebDraftPostAsync(request, model, analysis, cancellationToken),

            _ => Result.Success(new AgentChatCompletionResult(
                analysis.AssistantMessage ?? "Yeu cau nay nam ngoai pham vi scheduling assistant.",
                model,
                [],
                [],
                AgentActions.Unsupported))
        };
    }

    private async Task<Result<AgentChatCompletionResult>> CreateImageAndPostAsync(
        AgentChatRequest request,
        string model,
        AgentPromptAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var finalPrompt = string.IsNullOrWhiteSpace(analysis.FinalPrompt)
            ? request.Message
            : analysis.FinalPrompt.Trim();

        var postResult = await _mediator.Send(
            new CreatePostCommand(
                request.UserId,
                request.WorkspaceId,
                request.SessionId,
                null,
                BuildTitle(finalPrompt, analysis.Title),
                new PostContent
                {
                    Content = finalPrompt,
                    PostType = ResolvePostType(analysis.PostType, request.ImageOptions)
                },
                "waiting_for_image_generation"),
            cancellationToken);

        if (postResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(postResult.Error);
        }

        var imageResult = await _mediator.Send(
            new CreateChatImageCommand(
                request.UserId,
                request.SessionId,
                finalPrompt,
                [],
                postResult.Value.Id,
                request.ImageOptions?.Model,
                request.ImageOptions?.AspectRatio,
                request.ImageOptions?.Resolution,
                null,
                request.ImageOptions?.NumberOfVariances ?? 1,
                MapSocialTargets(request.ImageOptions)),
            cancellationToken);

        if (imageResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(imageResult.Error);
        }

        return Result.Success(new AgentChatCompletionResult(
            analysis.AssistantMessage ?? "Image generation started and a draft post was created for later scheduling.",
            model,
            ["create_post", "create_chat_image"],
            [
                new AgentActionResponse(
                    "post_create",
                    "create_post",
                    "completed",
                    "post",
                    postResult.Value.Id,
                    postResult.Value.Title,
                    "Draft post created and will be updated with generated images after callback.",
                    DateTime.UtcNow),
                new AgentActionResponse(
                    "image_create",
                    "create_chat_image",
                    "completed",
                    "chat",
                    imageResult.Value.ChatId,
                    Summary: "Image generation started for the current scheduling workflow.",
                    OccurredAt: DateTime.UtcNow)
            ],
            AgentActions.ImageAndPostCreated,
            null,
            analysis.RevisedPrompt,
            postResult.Value.Id,
            imageResult.Value.ChatId,
            imageResult.Value.CorrelationId));
    }

    private async Task<Result<AgentChatCompletionResult>> CreateDraftPostAsync(
        AgentChatRequest request,
        string model,
        AgentPromptAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var finalPrompt = string.IsNullOrWhiteSpace(analysis.FinalPrompt)
            ? request.Message
            : analysis.FinalPrompt.Trim();

        var postResult = await _mediator.Send(
            new CreatePostCommand(
                request.UserId,
                request.WorkspaceId,
                request.SessionId,
                null,
                BuildTitle(finalPrompt, analysis.Title),
                new PostContent
                {
                    Content = finalPrompt,
                    PostType = NormalizePostType(analysis.PostType)
                },
                "draft"),
            cancellationToken);

        if (postResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(postResult.Error);
        }

        return Result.Success(new AgentChatCompletionResult(
            analysis.AssistantMessage ?? "Draft post created for scheduling.",
            model,
            ["create_post"],
            [
                new AgentActionResponse(
                    "post_create",
                    "create_post",
                    "completed",
                    "post",
                    postResult.Value.Id,
                    postResult.Value.Title,
                    "Draft post created in the current workspace for later scheduling.",
                    DateTime.UtcNow)
            ],
            AgentActions.PostCreated,
            null,
            analysis.RevisedPrompt,
            postResult.Value.Id));
    }

    private async Task<Result<AgentChatCompletionResult>> CreateWebDraftPostAsync(
        AgentChatRequest request,
        string model,
        AgentPromptAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var finalPrompt = string.IsNullOrWhiteSpace(analysis.FinalPrompt)
            ? request.Message
            : analysis.FinalPrompt.Trim();

        var webPostResult = await _chatWebPostService.CreateDraftAsync(
            new ChatWebPostRequest(
                request.UserId,
                request.SessionId,
                request.WorkspaceId,
                finalPrompt,
                analysis.Title,
                analysis.PostType,
                request.AssistantChatId),
            cancellationToken);

        if (webPostResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(webPostResult.Error);
        }

        var toolNames = new List<string>();
        if (string.Equals(webPostResult.Value.RetrievalMode, "direct_url", StringComparison.OrdinalIgnoreCase))
        {
            toolNames.Add("fetch_url");
        }
        else
        {
            toolNames.Add("web_search");
        }

        if (webPostResult.Value.ImportedResourceIds.Count > 0)
        {
            toolNames.Add("import_media");
        }

        toolNames.Add("create_post");

        var actionSummary = webPostResult.Value.SourceUrls.Count == 0
            ? "Draft post created from retrieved web content."
            : $"Draft post created from {webPostResult.Value.SourceUrls.Count} web source(s).";

        return Result.Success(new AgentChatCompletionResult(
            analysis.AssistantMessage ?? "Draft post created from web content.",
            model,
            toolNames,
            [
                new AgentActionResponse(
                    "web_post_create",
                    webPostResult.Value.RetrievalMode,
                    "completed",
                    "post",
                    webPostResult.Value.PostId,
                    webPostResult.Value.Title,
                    actionSummary,
                    DateTime.UtcNow)
            ],
            AgentActions.WebPostCreated,
            null,
            analysis.RevisedPrompt,
            webPostResult.Value.PostId,
            null,
            null,
            webPostResult.Value.RetrievalMode,
            webPostResult.Value.SourceUrls,
            webPostResult.Value.ImportedResourceIds));
    }

    private async Task<Result<AgentPromptAnalysis>> AnalyzePromptAsync(
        string message,
        string model,
        CancellationToken cancellationToken)
    {
        var chatClient = CreateClient().AsIChatClient(model);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildAnalyzerPrompt()),
            new(ChatRole.User, message)
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var content = response.Messages
                .LastOrDefault(item => item.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(item.Text))
                ?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure<AgentPromptAnalysis>(AgentErrors.EmptyResponse);
            }

            var payload = ExtractJsonPayload(content);
            var analysis = JsonSerializer.Deserialize<AgentPromptAnalysis>(payload, JsonOptions);
            if (analysis is null || string.IsNullOrWhiteSpace(analysis.Action))
            {
                return Result.Failure<AgentPromptAnalysis>(
                    new Error("Agent.InvalidModelResponse", "Agent analyzer did not return a valid JSON decision."));
            }

            return Result.Success(analysis with
            {
                Action = NormalizeAction(analysis.Action)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini agent analysis failed for SessionId {SessionId}", message);
            return Result.Failure<AgentPromptAnalysis>(
                new Error("Agent.RequestFailed", $"Gemini agent request failed: {ex.Message}"));
        }
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

    private Client CreateClient()
    {
        var apiKey = _credentialProvider.GetOptionalValue("Gemini", "ApiKey");
        return string.IsNullOrWhiteSpace(apiKey)
            ? new Client()
            : new Client(apiKey: apiKey);
    }

    private static string BuildAnalyzerPrompt()
    {
        return
            """
            You are MeAI's restricted scheduling assistant.

            Scope:
            - Read exactly one user message.
            - Do not assume any prior chat history exists.
            - Do not plan schedules, choose schedule time, or act like a general-purpose assistant.
            - Your only job is to decide whether the latest message is clear enough for:
              1. image generation for future scheduling content, or
              2. draft-post creation for future scheduling content, or
              3. creating a draft post from a URL or web search result.

            Rules:
            - If the request is ambiguous or contains unresolved personal references such as "doi bong toi yeu", "nguoi toi thich", "thuong hieu cua toi", return action "validation_failed".
            - When action is "validation_failed", provide both:
              - validationError: one short sentence
              - revisedPrompt: a corrected prompt using placeholders like {{ten doi bong}}
            - If the request is a clear image-generation request, return action "image_created".
            - If the request is a clear request to create post/caption/content for later scheduling, return action "post_created".
            - If the request clearly asks to create a post from one or more URLs, a webpage, an article, news, or web search results, return action "web_post_created".
            - If the request is outside this narrow scheduling-content scope, return action "unsupported".
            - assistantMessage must be concise and practical.
            - finalPrompt should contain the cleaned prompt to use for image generation or draft-post creation.
            - title should be short and optional.
            - postType should be "posts" or "reels" only when action is "post_created" or "web_post_created".

            Output:
            Return JSON only. No markdown fences.

            Schema:
            {
              "action": "validation_failed" | "image_created" | "post_created" | "web_post_created" | "unsupported",
              "assistantMessage": "string",
              "validationError": "string or null",
              "revisedPrompt": "string or null",
              "finalPrompt": "string or null",
              "title": "string or null",
              "postType": "posts" | "reels" | null
            }
            """;
    }

    private static AgentChatCompletionResult? TryBuildObviousValidation(string message)
    {
        var normalized = message.Trim();
        if (normalized.Contains("doi bong toi yeu", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentChatCompletionResult(
                "Yeu cau chua ro doi bong nao. Hay thay phan mo ho bang ten doi bong cu the.",
                null,
                [],
                [],
                AgentActions.ValidationFailed,
                "Yeu cau chua xac dinh doi bong nao.",
                normalized.Replace(
                    "doi bong toi yeu",
                    "doi bong {{ten doi bong}}",
                    StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string ExtractJsonPayload(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed.Trim('`').Trim();
    }

    private static string NormalizeAction(string? action)
    {
        return (action ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "validation_failed" => AgentActions.ValidationFailed,
            "image_request" => AgentActions.ImageCreated,
            "image_created" => AgentActions.ImageCreated,
            "draft_post" => AgentActions.PostCreated,
            "post_created" => AgentActions.PostCreated,
            "web_post" => AgentActions.WebPostCreated,
            "web_post_created" => AgentActions.WebPostCreated,
            _ => AgentActions.Unsupported
        };
    }

    private static string NormalizePostType(string? postType)
    {
        return string.Equals((postType ?? string.Empty).Trim(), "reels", StringComparison.OrdinalIgnoreCase)
            ? "reels"
            : "posts";
    }

    private static string ResolvePostType(string? suggestedPostType, AgentImageOptions? imageOptions)
    {
        var fromAnalysis = NormalizePostType(suggestedPostType);
        if (imageOptions?.SocialTargets is not { Count: > 0 })
        {
            return fromAnalysis;
        }

        return imageOptions.SocialTargets.Any(target =>
            string.Equals(target.Type, "reel", StringComparison.OrdinalIgnoreCase))
            ? "reels"
            : fromAnalysis;
    }

    private static IReadOnlyList<SocialTargetDto>? MapSocialTargets(AgentImageOptions? imageOptions)
    {
        if (imageOptions?.SocialTargets is not { Count: > 0 })
        {
            return null;
        }

        return imageOptions.SocialTargets
            .Where(target =>
                !string.IsNullOrWhiteSpace(target.Platform) &&
                !string.IsNullOrWhiteSpace(target.Type) &&
                !string.IsNullOrWhiteSpace(target.Ratio))
            .Select(target => new SocialTargetDto
            {
                Platform = target.Platform.Trim(),
                Type = target.Type.Trim(),
                Ratio = target.Ratio.Trim()
            })
            .ToList();
    }

    private static string BuildTitle(string finalPrompt, string? suggestedTitle)
    {
        if (!string.IsNullOrWhiteSpace(suggestedTitle))
        {
            return suggestedTitle.Trim();
        }

        var compact = string.Join(' ', finalPrompt
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return compact.Length <= 80 ? compact : compact[..80].TrimEnd();
    }

    private static class AgentActions
    {
        public const string ValidationFailed = "validation_failed";
        public const string ImageCreated = "image_created";
        public const string ImageAndPostCreated = "image_and_post_created";
        public const string PostCreated = "post_created";
        public const string WebPostCreated = "web_post_created";
        public const string Unsupported = "unsupported";
    }

    private sealed record AgentPromptAnalysis(
        string Action,
        string? AssistantMessage,
        string? ValidationError,
        string? RevisedPrompt,
        string? FinalPrompt,
        string? Title,
        string? PostType);
}
