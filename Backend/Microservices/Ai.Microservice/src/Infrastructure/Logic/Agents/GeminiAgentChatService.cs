using System.Text.Json;
using Application.Abstractions.Agents;
using Application.Abstractions.Configs;
using Application.Agents;
using Application.Agents.Models;
using Application.Chats.Commands;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
using Application.Posts.Commands;
using Application.Recommendations.Services;
using Domain.Entities;
using Infrastructure.Logic.Kie;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.ImageGenerating;

namespace Infrastructure.Logic.Agents;

public sealed class GeminiAgentChatService : IAgentChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly IUserConfigService _userConfigService;
    private readonly IMediator _mediator;
    private readonly ILogger<GeminiAgentChatService> _logger;
    private readonly KieResponsesClient _kieResponsesClient;
    private readonly IChatWebPostService _chatWebPostService;
    private readonly IQueryRewriter _queryRewriter;

    public GeminiAgentChatService(
        IConfiguration configuration,
        KieResponsesClient kieResponsesClient,
        IUserConfigService userConfigService,
        IMediator mediator,
        IChatWebPostService chatWebPostService,
        IQueryRewriter queryRewriter,
        ILogger<GeminiAgentChatService> logger)
    {
        _configuration = configuration;
        _kieResponsesClient = kieResponsesClient;
        _userConfigService = userConfigService;
        _mediator = mediator;
        _chatWebPostService = chatWebPostService;
        _queryRewriter = queryRewriter;
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

        if (request.ScheduleOptions is not null)
        {
            var inferredFutureEventIntent = TryBuildFutureEventScheduleInference(request.Message);
            return await CreateFutureAiScheduleAsync(request, inferredFutureEventIntent, cancellationToken);
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

            AgentActions.VideoCreated => await CreateVideoAndPostAsync(request, model, analysis, cancellationToken),

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

    private async Task<Result<AgentChatCompletionResult>> CreateFutureAiScheduleAsync(
        AgentChatRequest request,
        AgentPromptAnalysis? inferredAnalysis,
        CancellationToken cancellationToken)
    {
        var scheduleOptions = request.ScheduleOptions!;
        var validationError = ValidateScheduleOptions(scheduleOptions);
        if (validationError is not null)
        {
            return Result.Failure<AgentChatCompletionResult>(validationError);
        }

        var model = await ResolveModelAsync(cancellationToken);
        AgentPromptAnalysis analysis;
        if (inferredAnalysis is not null)
        {
            analysis = inferredAnalysis;
        }
        else
        {
            var analysisResult = await AnalyzePromptAsync(request.Message, model, cancellationToken);
            if (analysisResult.IsFailure)
            {
                return Result.Failure<AgentChatCompletionResult>(analysisResult.Error);
            }

            analysis = analysisResult.Value;
        }

        if (string.Equals(analysis.Action, AgentActions.ValidationFailed, StringComparison.Ordinal))
        {
            return Result.Success(new AgentChatCompletionResult(
                analysis.AssistantMessage ?? "Yeu cau chua du ro de tao lich dang AI.",
                model,
                [],
                [],
                analysis.Action,
                analysis.ValidationError,
                analysis.RevisedPrompt));
        }

        if (string.Equals(analysis.Action, AgentActions.ImageCreated, StringComparison.Ordinal))
        {
            return Result.Success(new AgentChatCompletionResult(
                "Flow nay chi ho tro schedule AI tao bai dang tuong lai, khong ho tro tao anh truoc luc schedule.",
                model,
                [],
                [],
                AgentActions.Unsupported));
        }

        if (string.Equals(analysis.Action, AgentActions.VideoCreated, StringComparison.Ordinal))
        {
            return Result.Success(new AgentChatCompletionResult(
                "Flow nay chi ho tro schedule AI tao bai dang tuong lai, khong ho tro tao video truoc luc schedule.",
                model,
                [],
                [],
                AgentActions.Unsupported));
        }

        if (string.Equals(analysis.Action, AgentActions.Unsupported, StringComparison.Ordinal))
        {
            return Result.Success(new AgentChatCompletionResult(
                analysis.AssistantMessage ?? "Yeu cau nay nam ngoai pham vi future AI scheduling.",
                model,
                [],
                [],
                AgentActions.Unsupported));
        }

        var finalPrompt = string.IsNullOrWhiteSpace(analysis.FinalPrompt)
            ? request.Message
            : analysis.FinalPrompt.Trim();
        var platformPreference = ResolvePlatformPreference(scheduleOptions.Targets);
        var rewriteResult = await _queryRewriter.RewriteAsync(
            new QueryRewriteRequest(
                finalPrompt,
                Platform: platformPreference),
            cancellationToken);

        if (rewriteResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(rewriteResult.Error);
        }

        var normalizedSearch = new PublishingScheduleSearchInput(
            rewriteResult.Value.PrimaryQuery,
            5,
            null,
            string.IsNullOrWhiteSpace(rewriteResult.Value.Language) ? null : rewriteResult.Value.Language,
            "pd");

        var scheduleResult = await _mediator.Send(
            new CreateAgenticPublishingScheduleCommand(
                request.UserId,
                request.WorkspaceId,
                BuildTitle(finalPrompt, analysis.Title),
                "agentic",
                NormalizeScheduleExecutionTime(scheduleOptions.ExecuteAtUtc),
                scheduleOptions.Timezone,
                false,
                platformPreference,
                finalPrompt,
                scheduleOptions.MaxContentLength,
                normalizedSearch,
                scheduleOptions.Targets),
            cancellationToken);

        if (scheduleResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(scheduleResult.Error);
        }

        return Result.Success(new AgentChatCompletionResult(
            analysis.AssistantMessage ?? "Future AI schedule created. Content will be generated at runtime from fresh web search and RAG grounding.",
            model,
            ["create_agentic_schedule"],
            [
                new AgentActionResponse(
                    "schedule_create",
                    "create_agentic_schedule",
                    "completed",
                    "schedule",
                    scheduleResult.Value.Id,
                    scheduleResult.Value.Name,
                    "Future AI schedule created. No draft post was generated up front.",
                    DateTime.UtcNow)
            ],
            AgentActions.FutureAiScheduleCreated,
            null,
            analysis.RevisedPrompt,
            null,
            scheduleResult.Value.Id));
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
                "waiting_for_image_generation",
                null,
                null,
                PostBuilderOriginKinds.AiOther),
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
            PostId: postResult.Value.Id,
            PostBuilderId: postResult.Value.PostBuilderId,
            PostIds: [postResult.Value.Id],
            ChatId: imageResult.Value.ChatId,
            CorrelationId: imageResult.Value.CorrelationId));
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
                "draft",
                null,
                null,
                PostBuilderOriginKinds.AiOther),
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
            PostId: postResult.Value.Id,
            PostBuilderId: postResult.Value.PostBuilderId,
            PostIds: [postResult.Value.Id]));
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
            PostId: webPostResult.Value.PostId,
            RetrievalMode: webPostResult.Value.RetrievalMode,
            SourceUrls: webPostResult.Value.SourceUrls,
            ImportedResourceIds: webPostResult.Value.ImportedResourceIds,
            PostBuilderId: webPostResult.Value.PostBuilderId,
            PostIds: [webPostResult.Value.PostId]));
    }

    private async Task<Result<AgentChatCompletionResult>> CreateVideoAndPostAsync(
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
                    PostType = "reels"
                },
                "waiting_for_video_generation",
                null,
                null,
                PostBuilderOriginKinds.AiOther),
            cancellationToken);

        if (postResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(postResult.Error);
        }

        var videoResult = await _mediator.Send(
            new CreateChatVideoCommand(
                request.UserId,
                request.SessionId,
                finalPrompt,
                request.VideoOptions?.ResourceIds ?? [],
                request.VideoOptions?.Model,
                request.VideoOptions?.AspectRatio,
                request.VideoOptions?.Seeds,
                request.VideoOptions?.EnableTranslation,
                request.VideoOptions?.Watermark,
                postResult.Value.Id),
            cancellationToken);

        if (videoResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(videoResult.Error);
        }

        return Result.Success(new AgentChatCompletionResult(
            analysis.AssistantMessage ?? "Video generation started and a draft post was created for later scheduling.",
            model,
            ["create_post", "create_chat_video"],
            [
                new AgentActionResponse(
                    "post_create",
                    "create_post",
                    "completed",
                    "post",
                    postResult.Value.Id,
                    postResult.Value.Title,
                    "Draft post created and will be updated with generated video after callback.",
                    DateTime.UtcNow),
                new AgentActionResponse(
                    "video_create",
                    "create_chat_video",
                    "completed",
                    "chat",
                    videoResult.Value.ChatId,
                    Summary: "Video generation started for the current workflow.",
                    OccurredAt: DateTime.UtcNow)
            ],
            AgentActions.VideoAndPostCreated,
            null,
            analysis.RevisedPrompt,
            PostId: postResult.Value.Id,
            PostBuilderId: postResult.Value.PostBuilderId,
            PostIds: [postResult.Value.Id],
            ChatId: videoResult.Value.ChatId,
            CorrelationId: videoResult.Value.CorrelationId));
    }

    private async Task<Result<AgentPromptAnalysis>> AnalyzePromptAsync(
        string message,
        string model,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _kieResponsesClient.GetFunctionArgumentsAsync(
                model,
                [KieResponsesClient.UserText($"{BuildAnalyzerPrompt()}\n\nUser message:\n{message}")],
                BuildAnalyzeScheduleRequestTool(),
                "Agent.RequestFailed",
                "Kie agent request failed.",
                cancellationToken);

            if (response.IsFailure)
            {
                return Result.Failure<AgentPromptAnalysis>(response.Error);
            }

            var content = response.Value.Trim();
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
            _logger.LogError(ex, "Kie agent analysis failed for message {Message}", message);
            return Result.Failure<AgentPromptAnalysis>(
                new Error("Agent.RequestFailed", $"Kie agent request failed: {ex.Message}"));
        }
    }

    private async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var activeConfigResult = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        var configuredModel = _configuration["Kie:ChatModel"]
                              ?? _configuration["Kie__ChatModel"];

        return KieResponsesClient.ResolveResponsesModel(
            activeConfigResult.IsSuccess ? activeConfigResult.Value?.ChatModel : null,
            configuredModel);
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
              2. video generation for future scheduling content, or
              3. draft-post creation for future scheduling content, or
              4. creating a draft post from a URL or web search result.

            Rules:
            - If the request is ambiguous or contains unresolved personal references such as "doi bong toi yeu", "nguoi toi thich", "thuong hieu cua toi", return action "validation_failed".
            - Prefer making reasonable assumptions when the user intent is already clear enough for future scheduling content.
            - If the missing detail is a future result that will only be known at runtime, do NOT fail validation. Rewrite the prompt so it refers to the real-world outcome that will be fetched later.
            - Example: "dang bai ve doi tuyen chien thang world cup nam nay" is valid for future scheduling. Rewrite it into a prompt about "doi tuyen vo dich World Cup nam nay" and continue.
            - Only return "validation_failed" when the missing detail is truly blocking and cannot be inferred safely, such as a personal reference ("doi bong toi yeu") or a missing event/topic.
            - When action is "validation_failed", provide both:
              - validationError: one short sentence
              - revisedPrompt: a corrected prompt using placeholders like {{ten doi bong}}
            - If the request is a clear image-generation request, return action "image_created".
            - If the request is a clear video-generation request, return action "video_created".
            - If the request is a clear request to create post/caption/content for later scheduling, return action "post_created".
            - If the request clearly asks to create a post from one or more URLs, a webpage, an article, news, or web search results, return action "web_post_created".
            - If the request is outside this narrow scheduling-content scope, return action "unsupported".
            - assistantMessage must be concise and practical.
            - finalPrompt should contain the cleaned prompt to use for image generation or draft-post creation.
            - title should be short and optional.
            - postType should be "posts" or "reels" only when action is "post_created" or "web_post_created".

            Output:
            Call the analyze_schedule_request tool with your decision. Do not answer in text.
            """;
    }

    private static KieResponsesFunctionTool BuildAnalyzeScheduleRequestTool()
    {
        return new KieResponsesFunctionTool
        {
            Name = "analyze_schedule_request",
            Description = "Classify and normalize a user request for MeAI's restricted scheduling assistant.",
            Parameters = new
            {
                type = "object",
                additionalProperties = false,
                required = new[]
                {
                    "action",
                    "assistantMessage",
                    "validationError",
                    "revisedPrompt",
                    "finalPrompt",
                    "title",
                    "postType"
                },
                properties = new
                {
                    action = new
                    {
                        type = "string",
                        @enum = new[]
                        {
                            "validation_failed",
                            "image_created",
                            "video_created",
                            "post_created",
                            "web_post_created",
                            "unsupported"
                        }
                    },
                    assistantMessage = new
                    {
                        type = new[] { "string", "null" },
                        description = "Concise practical message to show the user."
                    },
                    validationError = new
                    {
                        type = new[] { "string", "null" },
                        description = "Reason when action is validation_failed, otherwise null."
                    },
                    revisedPrompt = new
                    {
                        type = new[] { "string", "null" },
                        description = "Corrected prompt when available."
                    },
                    finalPrompt = new
                    {
                        type = new[] { "string", "null" },
                        description = "Clean prompt to use for image generation or draft creation."
                    },
                    title = new
                    {
                        type = new[] { "string", "null" },
                        description = "Short optional title."
                    },
                    postType = new
                    {
                        type = new[] { "string", "null" },
                        @enum = new object?[] { "posts", "reels", null },
                        description = "posts or reels only when action creates content, otherwise null."
                    }
                }
            }
        };
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

    private static AgentPromptAnalysis? TryBuildFutureEventScheduleInference(string message)
    {
        var normalized = NormalizeForInference(message);
        if (!ContainsAny(normalized, "world cup", "worldcup"))
        {
            return null;
        }

        if (!ContainsAny(
                normalized,
                "chien thang",
                "vo dich",
                "thang cuoc",
                "winner",
                "champion"))
        {
            return null;
        }

        var rewrittenPrompt = RewriteWorldCupWinnerPrompt(message);
        return new AgentPromptAnalysis(
            AgentActions.PostCreated,
            "Tôi sẽ hiểu đây là một bài đăng runtime về đội tuyển vô địch World Cup khi đến thời điểm chạy.",
            null,
            rewrittenPrompt,
            rewrittenPrompt,
            "Bài đăng đội tuyển vô địch World Cup",
            "posts");
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
            "video_request" => AgentActions.VideoCreated,
            "video_created" => AgentActions.VideoCreated,
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

    private static Error? ValidateScheduleOptions(AgentScheduleOptions options)
    {
        if (options.Targets is null || options.Targets.Count == 0)
        {
            return PublishingScheduleErrors.MissingTargets;
        }

        if (string.IsNullOrWhiteSpace(options.Timezone))
        {
            return PublishingScheduleErrors.InvalidTimezone;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(options.Timezone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return PublishingScheduleErrors.InvalidTimezone;
        }
        catch (InvalidTimeZoneException)
        {
            return PublishingScheduleErrors.InvalidTimezone;
        }

        if (NormalizeScheduleExecutionTime(options.ExecuteAtUtc) <= DateTime.UtcNow)
        {
            return PublishingScheduleErrors.ExecuteAtInPast;
        }

        if (!options.MaxContentLength.HasValue)
        {
            return PublishingScheduleErrors.MaxContentLengthRequired;
        }

        if (options.MaxContentLength.Value < 1 || options.MaxContentLength.Value > 10000)
        {
            return PublishingScheduleErrors.InvalidMaxContentLength;
        }

        return null;
    }

    private static DateTime NormalizeScheduleExecutionTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string? ResolvePlatformPreference(IReadOnlyList<PublishingScheduleTargetInput>? targets)
    {
        return null;
    }

    private static string RewriteWorldCupWinnerPrompt(string message)
    {
        var rewritten = message;
        rewritten = rewritten.Replace("chiến thắng", "vô địch", StringComparison.OrdinalIgnoreCase);
        rewritten = rewritten.Replace("chien thang", "vo dich", StringComparison.OrdinalIgnoreCase);
        rewritten = rewritten.Replace("thắng cuộc", "vô địch", StringComparison.OrdinalIgnoreCase);
        rewritten = rewritten.Replace("thang cuoc", "vo dich", StringComparison.OrdinalIgnoreCase);
        rewritten = rewritten.Replace("winner", "champion", StringComparison.OrdinalIgnoreCase);

        if (rewritten.IndexOf("đội tuyển", StringComparison.OrdinalIgnoreCase) >= 0 &&
            rewritten.IndexOf("vô địch", StringComparison.OrdinalIgnoreCase) < 0 &&
            rewritten.IndexOf("vo dich", StringComparison.OrdinalIgnoreCase) < 0)
        {
            rewritten = rewritten.Replace("đội tuyển", "đội tuyển vô địch", StringComparison.OrdinalIgnoreCase);
            rewritten = rewritten.Replace("doi tuyen", "doi tuyen vo dich", StringComparison.OrdinalIgnoreCase);
        }

        if (!rewritten.Contains("thời điểm chạy", StringComparison.OrdinalIgnoreCase) &&
            !rewritten.Contains("runtime", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = $"{rewritten.Trim().TrimEnd('.')} dựa trên kết quả thực tế tại thời điểm chạy.";
        }

        return rewritten;
    }

    private static string NormalizeForInference(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static bool ContainsAny(string text, params string[] candidates)
    {
        return candidates.Any(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static class AgentActions
    {
        public const string ValidationFailed = "validation_failed";
        public const string ImageCreated = "image_created";
        public const string ImageAndPostCreated = "image_and_post_created";
        public const string VideoCreated = "video_created";
        public const string VideoAndPostCreated = "video_and_post_created";
        public const string PostCreated = "post_created";
        public const string WebPostCreated = "web_post_created";
        public const string FutureAiScheduleCreated = "future_ai_schedule_created";
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
