using System.Text.Json;
using Application.Abstractions.Agents;
using Application.Abstractions.Configs;
using Application.Abstractions.SocialMedias;
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
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IMediator _mediator;
    private readonly ILogger<GeminiAgentChatService> _logger;
    private readonly KieResponsesClient _kieResponsesClient;
    private readonly IChatWebPostService _chatWebPostService;
    private readonly IQueryRewriter _queryRewriter;

    public GeminiAgentChatService(
        IConfiguration configuration,
        KieResponsesClient kieResponsesClient,
        IUserConfigService userConfigService,
        IUserSocialMediaService userSocialMediaService,
        IMediator mediator,
        IChatWebPostService chatWebPostService,
        IQueryRewriter queryRewriter,
        ILogger<GeminiAgentChatService> logger)
    {
        _configuration = configuration;
        _kieResponsesClient = kieResponsesClient;
        _userConfigService = userConfigService;
        _userSocialMediaService = userSocialMediaService;
        _mediator = mediator;
        _chatWebPostService = chatWebPostService;
        _queryRewriter = queryRewriter;
        _logger = logger;
    }

    public async Task<Result<AgentChatCompletionResult>> GenerateReplyAsync(
        AgentChatRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Agent chat request received. UserId={UserId} WorkspaceId={WorkspaceId} SessionId={SessionId} AssistantChatId={AssistantChatId} HasScheduleOptions={HasScheduleOptions} MessagePreview={MessagePreview}",
            request.UserId,
            request.WorkspaceId,
            request.SessionId,
            request.AssistantChatId,
            request.ScheduleOptions is not null,
            Preview(request.Message));

        if (request.ScheduleOptions is not null)
        {
            return await CreateFutureAiScheduleAsync(request, null, cancellationToken);
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

        _logger.LogInformation(
            "Future AI schedule analysis resolved. Action={Action} ValidationError={ValidationError} FinalPromptPreview={FinalPromptPreview} Title={Title} PostType={PostType}",
            analysis.Action,
            analysis.ValidationError ?? "<none>",
            Preview(analysis.FinalPrompt),
            analysis.Title ?? "<none>",
            analysis.PostType ?? "<none>");

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
        var targetPlatformsResult = await ResolveScheduleTargetPlatformsAsync(
            request.UserId,
            scheduleOptions.Targets,
            cancellationToken);
        if (targetPlatformsResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(targetPlatformsResult.Error);
        }

        var targetPlatforms = targetPlatformsResult.Value;
        var platformPreference = ResolvePlatformPreference(targetPlatforms);
        var desiredPostType = ResolveDesiredSchedulePostType(targetPlatforms, analysis.PostType);
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

        _logger.LogInformation(
            "Future AI schedule query rewrite completed. PlatformPreference={PlatformPreference} PrimaryQuery={PrimaryQuery} Language={Language} Intent={Intent} KeyTerms={KeyTerms}",
            platformPreference ?? "<none>",
            rewriteResult.Value.PrimaryQuery,
            rewriteResult.Value.Language ?? "<none>",
            rewriteResult.Value.Intent,
            string.Join(", ", rewriteResult.Value.KeyTerms));

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
                scheduleOptions.Targets,
                desiredPostType),
            cancellationToken);

        if (scheduleResult.IsFailure)
        {
            return Result.Failure<AgentChatCompletionResult>(scheduleResult.Error);
        }

        _logger.LogInformation(
            "Future AI schedule created. ScheduleId={ScheduleId} Name={ScheduleName} ExecuteAtUtc={ExecuteAtUtc} Timezone={Timezone}",
            scheduleResult.Value.Id,
            scheduleResult.Value.Name,
            scheduleResult.Value.ExecuteAtUtc,
            scheduleResult.Value.Timezone);

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
            _logger.LogInformation(
                "Calling Kie schedule analyzer. Model={Model} MessagePreview={MessagePreview}",
                model,
                Preview(message));

            var response = await _kieResponsesClient.GetFunctionArgumentsAsync(
                model,
                [
                    KieResponsesClient.DeveloperText(BuildAnalyzerPrompt()),
                    KieResponsesClient.UserText(message)
                ],
                BuildAnalyzeScheduleRequestTool(),
                "Agent.RequestFailed",
                "Kie agent request failed.",
                cancellationToken);

            if (response.IsFailure)
            {
                _logger.LogWarning(
                    "Kie schedule analyzer function-call mode failed. Model={Model} ErrorCode={ErrorCode} ErrorDescription={ErrorDescription}. Falling back to JSON-only mode.",
                    model,
                    response.Error.Code,
                    response.Error.Description);

                response = await _kieResponsesClient.GetTextResponseAsync(
                    model,
                    [
                        KieResponsesClient.DeveloperText(BuildAnalyzerJsonFallbackPrompt()),
                        KieResponsesClient.UserText(message)
                    ],
                    "Agent.RequestFailed",
                    "Kie agent request failed.",
                    cancellationToken);

                if (response.IsFailure)
                {
                    _logger.LogWarning(
                        "Kie schedule analyzer JSON-only fallback also failed. Model={Model} ErrorCode={ErrorCode} ErrorDescription={ErrorDescription}",
                        model,
                        response.Error.Code,
                        response.Error.Description);

                    return Result.Failure<AgentPromptAnalysis>(response.Error);
                }
            }

            var content = response.Value.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure<AgentPromptAnalysis>(AgentErrors.EmptyResponse);
            }

            var payload = ExtractJsonPayload(content);
            _logger.LogInformation(
                "Kie schedule analyzer returned payload. Model={Model} PayloadPreview={PayloadPreview}",
                model,
                Preview(payload));

            var analysis = JsonSerializer.Deserialize<AgentPromptAnalysis>(payload, JsonOptions);
            if (analysis is null || string.IsNullOrWhiteSpace(analysis.Action))
            {
                _logger.LogWarning(
                    "Kie schedule analyzer returned invalid payload. Model={Model} PayloadPreview={PayloadPreview}",
                    model,
                    Preview(payload));

                return Result.Failure<AgentPromptAnalysis>(
                    new Error("Agent.InvalidModelResponse", "Agent analyzer did not return a valid JSON decision."));
            }

            var normalizedAction = NormalizeAction(analysis.Action);
            _logger.LogInformation(
                "Kie schedule analyzer parsed successfully. Model={Model} Action={Action} ValidationError={ValidationError} FinalPromptPreview={FinalPromptPreview} Title={Title} PostType={PostType}",
                model,
                normalizedAction,
                analysis.ValidationError ?? "<none>",
                Preview(analysis.FinalPrompt),
                analysis.Title ?? "<none>",
                analysis.PostType ?? "<none>");

            return Result.Success(analysis with
            {
                Action = normalizedAction
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
        var preferredModel = activeConfigResult.IsSuccess ? activeConfigResult.Value?.ChatModel : null;
        var resolvedModel = KieResponsesClient.ResolveResponsesModel(
            preferredModel,
            configuredModel);

        _logger.LogInformation(
            "Resolved Kie model for agent chat. PreferredModel={PreferredModel} ConfiguredModel={ConfiguredModel} ResolvedModel={ResolvedModel} ConfigLookupSucceeded={ConfigLookupSucceeded}",
            preferredModel ?? "<none>",
            configuredModel ?? "<none>",
            resolvedModel,
            activeConfigResult.IsSuccess);

        return resolvedModel;
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
            - Requests that ask to monitor, track, follow, search, or summarize fresh news, trends, or developments at execution time are VALID future scheduling content.
            - Example: "theo doi cac dien bien AI noi bat trong 24 gio qua va viet 1 post" should return action "post_created", not "unsupported". Rewrite it so the 24-hour window is relative to execution time.
            - Example: "dang bai ve doi tuyen chien thang world cup nam nay" is valid for future scheduling. Rewrite it into a prompt about "doi tuyen vo dich World Cup nam nay" and continue.
            - Only return "validation_failed" when the missing detail is truly blocking and cannot be inferred safely, such as a personal reference ("doi bong toi yeu") or a missing event/topic.
            - Do NOT return "unsupported" just because the prompt depends on fresh external information that will be fetched at runtime.
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

    private static string BuildAnalyzerJsonFallbackPrompt()
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
            - Requests that ask to monitor, track, follow, search, or summarize fresh news, trends, or developments at execution time are VALID future scheduling content.
            - Example: "theo doi cac dien bien AI noi bat trong 24 gio qua va viet 1 post" should return action "post_created", not "unsupported". Rewrite it so the 24-hour window is relative to execution time.
            - Example: "dang bai ve doi tuyen chien thang world cup nam nay" is valid for future scheduling. Rewrite it into a prompt about "doi tuyen vo dich World Cup nam nay" and continue.
            - Only return "validation_failed" when the missing detail is truly blocking and cannot be inferred safely, such as a personal reference ("doi bong toi yeu") or a missing event/topic.
            - Do NOT return "unsupported" just because the prompt depends on fresh external information that will be fetched at runtime.
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
            Return JSON only. Do not use markdown fences. Do not add explanation text.
            Required JSON object shape:
            {
              "action": "validation_failed | image_created | video_created | post_created | web_post_created | unsupported",
              "assistantMessage": "string or null",
              "validationError": "string or null",
              "revisedPrompt": "string or null",
              "finalPrompt": "string or null",
              "title": "string or null",
              "postType": "posts | reels | null"
            }
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

    private async Task<Result<IReadOnlyList<string>>> ResolveScheduleTargetPlatformsAsync(
        Guid userId,
        IReadOnlyList<PublishingScheduleTargetInput>? targets,
        CancellationToken cancellationToken)
    {
        if (targets is null || targets.Count == 0)
        {
            return Result.Success<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var socialMediaIds = targets
            .Select(target => target.SocialMediaId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (socialMediaIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var socialMediasResult = await _userSocialMediaService.GetSocialMediasAsync(
            userId,
            socialMediaIds,
            cancellationToken);
        if (socialMediasResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(socialMediasResult.Error);
        }

        var socialMediaById = socialMediasResult.Value.ToDictionary(item => item.SocialMediaId);
        var platforms = new List<string>(targets.Count);
        foreach (var target in targets)
        {
            if (!socialMediaById.TryGetValue(target.SocialMediaId, out var socialMedia))
            {
                return Result.Failure<IReadOnlyList<string>>(
                    new Error("SocialMedia.NotFound", "Social media account not found."));
            }

            platforms.Add(NormalizePlatform(socialMedia.Type));
        }

        return Result.Success<IReadOnlyList<string>>(platforms);
    }

    private static string? ResolvePlatformPreference(IReadOnlyList<string> targetPlatforms)
    {
        return targetPlatforms
            .FirstOrDefault(platform => !string.IsNullOrWhiteSpace(platform));
    }

    private static string ResolveDesiredSchedulePostType(
        IReadOnlyList<string> targetPlatforms,
        string? analyzedPostType)
    {
        if (targetPlatforms.Any(platform => string.Equals(platform, "tiktok", StringComparison.OrdinalIgnoreCase)))
        {
            return "reels";
        }

        if (targetPlatforms.Any(platform => string.Equals(platform, "threads", StringComparison.OrdinalIgnoreCase)))
        {
            return "posts";
        }

        return NormalizePostType(analyzedPostType);
    }

    private static string NormalizePlatform(string? platform)
    {
        return (platform ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string Preview(string? value, int maxLength = 1200)
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
