using System.ComponentModel;
using System.Text.Json;
using Application.Abstractions.Agents;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Workspaces;
using Application.Agents;
using Application.Agents.Models;
using Application.Posts.Commands;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using Google.GenAI;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Agents;

public sealed class GeminiAgentChatService : IAgentChatService
{
    private const string DefaultModel = "gemini-3.1-flash-lite-preview";
    private const int MaxHistoryMessages = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IConfiguration _configuration;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserWorkspaceService _userWorkspaceService;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IPostRepository _postRepository;
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IChatRepository _chatRepository;
    private readonly PublishingScheduleResponseBuilder _publishingScheduleResponseBuilder;
    private readonly IN8nWorkflowClient _n8nWorkflowClient;
    private readonly IMediator _mediator;
    private readonly ILogger<GeminiAgentChatService> _logger;
    private readonly IApiCredentialProvider _credentialProvider;

    public GeminiAgentChatService(
        IConfiguration configuration,
        IApiCredentialProvider credentialProvider,
        IUserConfigService userConfigService,
        IUserWorkspaceService userWorkspaceService,
        IUserSocialMediaService userSocialMediaService,
        IPostRepository postRepository,
        IPublishingScheduleRepository publishingScheduleRepository,
        IChatRepository chatRepository,
        PublishingScheduleResponseBuilder publishingScheduleResponseBuilder,
        IN8nWorkflowClient n8nWorkflowClient,
        IMediator mediator,
        ILogger<GeminiAgentChatService> logger)
    {
        _configuration = configuration;
        _credentialProvider = credentialProvider;
        _userConfigService = userConfigService;
        _userWorkspaceService = userWorkspaceService;
        _userSocialMediaService = userSocialMediaService;
        _postRepository = postRepository;
        _publishingScheduleRepository = publishingScheduleRepository;
        _chatRepository = chatRepository;
        _publishingScheduleResponseBuilder = publishingScheduleResponseBuilder;
        _n8nWorkflowClient = n8nWorkflowClient;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<AgentChatCompletionResult>> GenerateReplyAsync(
        AgentChatRequest request,
        CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(cancellationToken);
        var history = await BuildHistoryAsync(request.SessionId, request.WorkspaceId, cancellationToken);
        var tools = new AgentToolbox(
            request.UserId,
            request.SessionId,
            request.WorkspaceId,
            _userWorkspaceService,
            _userSocialMediaService,
            _postRepository,
            _publishingScheduleRepository,
            _publishingScheduleResponseBuilder,
            _n8nWorkflowClient,
            _mediator);

        var chatClient = CreateClient()
            .AsIChatClient(model)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    tools.GetUserWorkspacesAsync,
                    "get_user_workspaces",
                    "List the user's workspaces so you can resolve which workspace should be used."),
                AIFunctionFactory.Create(
                    tools.GetLinkedSocialAccountsAsync,
                    "get_linked_social_accounts",
                    "List the user's linked social accounts. Optionally filter by platform like facebook, instagram, threads, or tiktok."),
                AIFunctionFactory.Create(
                    tools.GetWorkspaceSocialAccountsAsync,
                    "get_workspace_social_accounts",
                    "List the social accounts linked to a workspace. If no workspaceId is provided, use the current chat session workspace."),
                AIFunctionFactory.Create(
                    tools.GetPostsAsync,
                    "get_posts",
                    "List existing posts for the user. Optionally filter by workspace and status."),
                AIFunctionFactory.Create(
                    tools.GetSchedulesAsync,
                    "get_schedules",
                    "List schedules for the user. Optionally filter by workspace and status."),
                AIFunctionFactory.Create(
                    tools.GetScheduleAsync,
                    "get_schedule",
                    "Get one schedule by id."),
                AIFunctionFactory.Create(
                    tools.CreatePostAsync,
                    "create_post",
                    "Create an editable draft post in MeAI and link it to the current chat session."),
                AIFunctionFactory.Create(
                    tools.UpdatePostAsync,
                    "update_post",
                    "Update an existing MeAI post draft and link it to the current chat session."),
                AIFunctionFactory.Create(
                    tools.CreateScheduleAsync,
                    "create_schedule",
                    "Create a fixed_content or agentic schedule. Use comma-separated GUID strings for targetSocialMediaIds and postIds."),
                AIFunctionFactory.Create(
                    tools.WebSearchAsync,
                    "web_search",
                    "Run a web search through n8n. Use this for live or time-sensitive information."),
                AIFunctionFactory.Create(
                    tools.GetCurrentTime,
                    "get_current_time",
                    "Get the current time in UTC or in a requested timezone.")
            ]
        };

        try
        {
            var response = await chatClient.GetResponseAsync(history, options, cancellationToken);
            var assistantMessage = response.Messages
                .LastOrDefault(message =>
                    message.Role == ChatRole.Assistant &&
                    !string.IsNullOrWhiteSpace(message.Text));

            var content = assistantMessage?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                content = response.Messages.LastOrDefault(message => !string.IsNullOrWhiteSpace(message.Text))
                    ?.Text?.Trim();
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure<AgentChatCompletionResult>(AgentErrors.EmptyResponse);
            }

            return Result.Success(new AgentChatCompletionResult(
                content,
                model,
                tools.GetInvokedToolNames(),
                tools.GetActions()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini agent chat failed for SessionId {SessionId}", request.SessionId);
            return Result.Failure<AgentChatCompletionResult>(
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

    private async Task<List<ChatMessage>> BuildHistoryAsync(
        Guid sessionId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var chats = await _chatRepository.GetBySessionIdAsync(sessionId, cancellationToken);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(workspaceId))
        };

        foreach (var chat in chats
                     .Where(item => !item.DeletedAt.HasValue && !string.IsNullOrWhiteSpace(item.Prompt))
                     .OrderBy(item => item.CreatedAt ?? DateTime.MinValue)
                     .ThenBy(item => item.Id)
                     .TakeLast(MaxHistoryMessages))
        {
            var metadata = AgentMessageConfigSerializer.Parse(chat.Config);
            var role = string.Equals(metadata.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.Assistant
                : ChatRole.User;

            messages.Add(new ChatMessage(role, chat.Prompt));
        }

        return messages;
    }

    private static string BuildSystemPrompt(Guid workspaceId)
    {
        return
            $"""
             You are MeAI's scheduling assistant.
             Your job is to help the user manage content planning inside MeAI and ask follow-up questions until the request is unambiguous.

             Rules:
             - Never guess a social account when there is ambiguity.
             - If the user asks for a future action, do not say it is already scheduled unless the system actually created a schedule.
             - Use tools to inspect workspaces, linked social accounts, workspace social accounts, and existing posts before asking the user to repeat information you can fetch.
             - If the user asks to create/save a post without a publish time, create an editable draft post with create_post. Do not claim drafts are unsupported.
             - If the user asks to edit a post created in this conversation, use update_post. If the post id is ambiguous, call get_posts and ask only if still ambiguous.
             - If the user has multiple candidate accounts for the same platform, ask which one should be used.
             - When talking about time-sensitive information, say that a later runtime web search will be needed rather than inventing live data.
             - Keep responses concise and practical.

             Current chat session workspace id: {workspaceId}
             """;
    }

    private Client CreateClient()
    {
        var apiKey = _credentialProvider.GetOptionalValue("Gemini", "ApiKey");
        return string.IsNullOrWhiteSpace(apiKey)
            ? new Client()
            : new Client(apiKey: apiKey);
    }

    private sealed class AgentToolbox
    {
        private readonly Guid _userId;
        private readonly Guid _sessionId;
        private readonly Guid _workspaceId;
        private readonly IUserWorkspaceService _userWorkspaceService;
        private readonly IUserSocialMediaService _userSocialMediaService;
        private readonly IPostRepository _postRepository;
        private readonly IPublishingScheduleRepository _publishingScheduleRepository;
        private readonly PublishingScheduleResponseBuilder _publishingScheduleResponseBuilder;
        private readonly IN8nWorkflowClient _n8nWorkflowClient;
        private readonly IMediator _mediator;
        private readonly List<string> _invokedToolNames = new();
        private readonly List<AgentActionResponse> _actions = new();

        public AgentToolbox(
            Guid userId,
            Guid sessionId,
            Guid workspaceId,
            IUserWorkspaceService userWorkspaceService,
            IUserSocialMediaService userSocialMediaService,
            IPostRepository postRepository,
            IPublishingScheduleRepository publishingScheduleRepository,
            PublishingScheduleResponseBuilder publishingScheduleResponseBuilder,
            IN8nWorkflowClient n8nWorkflowClient,
            IMediator mediator)
        {
            _userId = userId;
            _sessionId = sessionId;
            _workspaceId = workspaceId;
            _userWorkspaceService = userWorkspaceService;
            _userSocialMediaService = userSocialMediaService;
            _postRepository = postRepository;
            _publishingScheduleRepository = publishingScheduleRepository;
            _publishingScheduleResponseBuilder = publishingScheduleResponseBuilder;
            _n8nWorkflowClient = n8nWorkflowClient;
            _mediator = mediator;
        }

        public IReadOnlyList<string> GetInvokedToolNames()
        {
            return _invokedToolNames.Distinct(StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<AgentActionResponse> GetActions()
        {
            return _actions.ToArray();
        }

        public async Task<string> GetUserWorkspacesAsync()
        {
            Track("get_user_workspaces");

            var result = await _userWorkspaceService.GetWorkspacesAsync(_userId, CancellationToken.None);
            if (result.IsFailure)
            {
                RecordAction("tool_call", "get_user_workspaces", "failed", summary: result.Error.Description);
                return Serialize(new { error = result.Error.Description });
            }

            RecordAction(
                "tool_call",
                "get_user_workspaces",
                "completed",
                summary: $"Loaded {result.Value.Count} workspace(s).");

            return Serialize(result.Value.Select(item => new
            {
                workspaceId = item.WorkspaceId,
                name = item.Name,
                type = item.Type,
                description = item.Description,
                createdAt = item.CreatedAt,
                updatedAt = item.UpdatedAt
            }));
        }

        public async Task<string> GetLinkedSocialAccountsAsync(
            [Description("Optional platform filter like facebook, instagram, tiktok, or threads.")] string? platform = null)
        {
            Track("get_linked_social_accounts");

            var result = await _userSocialMediaService.GetSocialMediasByUserAsync(_userId, platform, CancellationToken.None);
            if (result.IsFailure)
            {
                RecordAction("tool_call", "get_linked_social_accounts", "failed", summary: result.Error.Description);
                return Serialize(new { error = result.Error.Description });
            }

            RecordAction(
                "tool_call",
                "get_linked_social_accounts",
                "completed",
                summary: $"Loaded {result.Value.Count} linked social account(s).");

            return Serialize(result.Value.Select(MapSocialSummary));
        }

        public async Task<string> GetWorkspaceSocialAccountsAsync(
            [Description("Optional workspace id. If omitted, the current chat session workspace is used.")] string? workspaceId = null)
        {
            Track("get_workspace_social_accounts");

            var resolvedWorkspaceId = ResolveWorkspaceId(workspaceId);
            if (resolvedWorkspaceId.IsFailure)
            {
                return Serialize(new { error = resolvedWorkspaceId.Error.Description });
            }

            var result = await _userSocialMediaService.GetWorkspaceSocialMediasAsync(
                _userId,
                resolvedWorkspaceId.Value,
                CancellationToken.None);

            if (result.IsFailure)
            {
                RecordAction("tool_call", "get_workspace_social_accounts", "failed", summary: result.Error.Description);
                return Serialize(new { error = result.Error.Description });
            }

            RecordAction(
                "tool_call",
                "get_workspace_social_accounts",
                "completed",
                entityType: "workspace",
                entityId: resolvedWorkspaceId.Value,
                summary: $"Loaded {result.Value.Count} workspace social account(s).");

            return Serialize(result.Value.Select(MapSocialSummary));
        }

        public async Task<string> GetPostsAsync(
            [Description("Optional workspace id. If omitted, the current chat session workspace is used.")] string? workspaceId = null,
            [Description("Optional post status filter like draft, scheduled, processing, or failed.")] string? status = null,
            [Description("If true, return only posts linked to the current chat session.")] bool currentChatOnly = false,
            [Description("Maximum number of posts to return.")] int limit = 20)
        {
            Track("get_posts");

            limit = Math.Clamp(limit, 1, 50);

            IReadOnlyList<Domain.Entities.Post> posts;
            if (currentChatOnly)
            {
                posts = await _postRepository.GetByUserIdAndChatSessionIdAsync(
                    _userId,
                    _sessionId,
                    null,
                    null,
                    limit,
                    CancellationToken.None);
            }
            else if (string.IsNullOrWhiteSpace(workspaceId))
            {
                posts = await _postRepository.GetByUserIdAndWorkspaceIdAsync(
                    _userId,
                    _workspaceId,
                    null,
                    null,
                    limit,
                    CancellationToken.None);
            }
            else
            {
                var resolvedWorkspaceId = ResolveWorkspaceId(workspaceId);
                if (resolvedWorkspaceId.IsFailure)
                {
                    RecordAction("tool_call", "get_posts", "failed", summary: resolvedWorkspaceId.Error.Description);
                    return Serialize(new { error = resolvedWorkspaceId.Error.Description });
                }

                posts = await _postRepository.GetByUserIdAndWorkspaceIdAsync(
                    _userId,
                    resolvedWorkspaceId.Value,
                    null,
                    null,
                    limit,
                    CancellationToken.None);
            }

            var filteredPosts = string.IsNullOrWhiteSpace(status)
                ? posts
                : posts.Where(post => string.Equals(post.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();

            RecordAction(
                "tool_call",
                "get_posts",
                "completed",
                currentChatOnly ? "chat_session" : "workspace",
                currentChatOnly ? _sessionId : _workspaceId,
                summary: $"Loaded {filteredPosts.Count()} post(s).");

            return Serialize(filteredPosts.Select(post => new
            {
                postId = post.Id,
                workspaceId = post.WorkspaceId,
                chatSessionId = post.ChatSessionId,
                title = post.Title,
                content = post.Content?.Content,
                hashtag = post.Content?.Hashtag,
                status = post.Status,
                platform = post.Platform,
                postType = post.Content?.PostType,
                scheduledAtUtc = post.ScheduledAtUtc,
                createdAt = post.CreatedAt,
                updatedAt = post.UpdatedAt
            }));
        }

        public string GetCurrentTime(
            [Description("Optional IANA timezone id like Asia/Ho_Chi_Minh.")] string? timezone = null)
        {
            Track("get_current_time");

            var utcNow = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(timezone))
            {
                RecordAction("tool_call", "get_current_time", "completed", summary: "Loaded current UTC time.");
                return Serialize(new
                {
                    utc = utcNow,
                    timezone = "UTC"
                });
            }

            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);

                RecordAction("tool_call", "get_current_time", "completed", summary: $"Loaded current time for {zone.Id}.");
                return Serialize(new
                {
                    utc = utcNow,
                    timezone = zone.Id,
                    localTime
                });
            }
            catch (TimeZoneNotFoundException)
            {
                RecordAction("tool_call", "get_current_time", "failed", summary: $"Timezone '{timezone}' was not found.");
                return Serialize(new { error = $"Timezone '{timezone}' was not found." });
            }
            catch (InvalidTimeZoneException)
            {
                RecordAction("tool_call", "get_current_time", "failed", summary: $"Timezone '{timezone}' is invalid.");
                return Serialize(new { error = $"Timezone '{timezone}' is invalid." });
            }
        }

        public async Task<string> GetSchedulesAsync(
            [Description("Optional workspace id. If omitted, all user schedules are returned.")] string? workspaceId = null,
            [Description("Optional schedule status filter like waiting_for_execution, scheduled, publishing, completed, or failed.")] string? status = null,
            [Description("Maximum number of schedules to return.")] int limit = 20)
        {
            Track("get_schedules");

            Guid? resolvedWorkspaceId = null;
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                var resolved = ResolveWorkspaceId(workspaceId);
                if (resolved.IsFailure)
                {
                    RecordAction("tool_call", "get_schedules", "failed", summary: resolved.Error.Description);
                    return Serialize(new { error = resolved.Error.Description });
                }

                resolvedWorkspaceId = resolved.Value;
            }

            var schedules = await _publishingScheduleRepository.GetByUserIdAsync(
                _userId,
                resolvedWorkspaceId,
                string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                Math.Clamp(limit, 1, 50),
                CancellationToken.None);

            var response = await _publishingScheduleResponseBuilder.BuildManyAsync(schedules, CancellationToken.None);
            RecordAction(
                "tool_call",
                "get_schedules",
                "completed",
                resolvedWorkspaceId.HasValue ? "workspace" : null,
                resolvedWorkspaceId,
                summary: $"Loaded {response.Count} schedule(s).");

            return Serialize(response.Select(schedule => new
            {
                scheduleId = schedule.Id,
                schedule.Name,
                schedule.Mode,
                schedule.Status,
                schedule.ExecuteAtUtc,
                schedule.Timezone,
                targetCount = schedule.Targets.Count,
                itemCount = schedule.Items.Count
            }));
        }

        public async Task<string> GetScheduleAsync(
            [Description("Schedule id as a GUID string.")] string scheduleId)
        {
            Track("get_schedule");

            if (!Guid.TryParse(scheduleId, out var parsedScheduleId) || parsedScheduleId == Guid.Empty)
            {
                RecordAction("tool_call", "get_schedule", "failed", "schedule", summary: "scheduleId must be a valid GUID.");
                return Serialize(new { error = "scheduleId must be a valid GUID." });
            }

            var schedule = await _publishingScheduleRepository.GetByIdAsync(parsedScheduleId, CancellationToken.None);
            if (schedule is null || schedule.DeletedAt.HasValue || schedule.UserId != _userId)
            {
                RecordAction("tool_call", "get_schedule", "failed", "schedule", parsedScheduleId, summary: "Publishing schedule not found.");
                return Serialize(new { error = "Publishing schedule not found." });
            }

            var response = await _publishingScheduleResponseBuilder.BuildAsync(schedule, CancellationToken.None);
            RecordAction("tool_call", "get_schedule", "completed", "schedule", response.Id, response.Name, response.Status);
            return Serialize(response);
        }

        public async Task<string> CreatePostAsync(
            [Description("Post body/content text.")] string content,
            [Description("Optional post title.")] string? title = null,
            [Description("Optional hashtag text, for example '#AI #Marketing'.")] string? hashtag = null,
            [Description("Post type: posts, reels, video, story. Defaults to posts.")] string? postType = null,
            [Description("Optional platform like facebook, instagram, threads, or tiktok.")] string? platform = null,
            [Description("Optional social media GUID. Only use this if the target account is known unambiguously.")] string? socialMediaId = null,
            [Description("Comma-separated resource GUIDs to attach to this post.")] string? resourceIds = null,
            [Description("Optional workspace id. Defaults to current chat workspace.")] string? workspaceId = null)
        {
            Track("create_post");

            if (string.IsNullOrWhiteSpace(content))
            {
                RecordAction("post_create", "create_post", "failed", "post", summary: "content is required.");
                return Serialize(new { error = "content is required." });
            }

            var resolvedWorkspaceId = ResolveWorkspaceId(workspaceId);
            if (resolvedWorkspaceId.IsFailure)
            {
                RecordAction("post_create", "create_post", "failed", "post", summary: resolvedWorkspaceId.Error.Description);
                return Serialize(new { error = resolvedWorkspaceId.Error.Description });
            }

            var socialMediaIdResult = ParseOptionalGuid(socialMediaId, "socialMediaId");
            if (socialMediaIdResult.IsFailure)
            {
                RecordAction("post_create", "create_post", "failed", "post", summary: socialMediaIdResult.Error.Description);
                return Serialize(new { error = socialMediaIdResult.Error.Description });
            }

            var resourceIdsResult = ParseGuidList(resourceIds, "resourceIds");
            if (resourceIdsResult.IsFailure)
            {
                RecordAction("post_create", "create_post", "failed", "post", summary: resourceIdsResult.Error.Description);
                return Serialize(new { error = resourceIdsResult.Error.Description });
            }

            var postContent = new PostContent
            {
                Content = content.Trim(),
                Hashtag = NormalizeOptionalString(hashtag),
                PostType = NormalizeOptionalString(postType) ?? "posts",
                ResourceList = resourceIdsResult.Value.Select(id => id.ToString()).ToList()
            };

            var duplicate = await FindRecentDraftDuplicateAsync(
                resolvedWorkspaceId.Value,
                title,
                postContent,
                platform,
                CancellationToken.None);

            if (duplicate is not null)
            {
                var updateResult = await _mediator.Send(
                    new UpdatePostCommand(
                        duplicate.Id,
                        _userId,
                        resolvedWorkspaceId.Value,
                        _sessionId,
                        socialMediaIdResult.Value,
                        title,
                        postContent,
                        "draft"),
                    CancellationToken.None);

                return updateResult.IsFailure
                    ? SerializeWithAction(
                        new { error = updateResult.Error.Description, code = updateResult.Error.Code },
                        "post_create",
                        "create_post",
                        "failed",
                        "post",
                        duplicate.Id,
                        duplicate.Title,
                        updateResult.Error.Description)
                    : SerializeWithAction(new
                    {
                        postId = updateResult.Value.Id,
                        updateResult.Value.ChatSessionId,
                        updateResult.Value.WorkspaceId,
                        updateResult.Value.Title,
                        updateResult.Value.Status,
                        deduplicated = true,
                        message = "A matching draft already existed in this chat session, so it was updated instead of creating a duplicate."
                    },
                        "post_update",
                        "create_post",
                        "completed",
                        "post",
                        updateResult.Value.Id,
                        updateResult.Value.Title,
                        "Updated an existing matching draft instead of creating a duplicate.");
            }

            var result = await _mediator.Send(
                new CreatePostCommand(
                    _userId,
                    resolvedWorkspaceId.Value,
                    _sessionId,
                    socialMediaIdResult.Value,
                    title,
                    postContent,
                    "draft",
                    null,
                    platform),
                CancellationToken.None);

            return result.IsFailure
                ? SerializeWithAction(
                    new { error = result.Error.Description, code = result.Error.Code },
                    "post_create",
                    "create_post",
                    "failed",
                    "post",
                    summary: result.Error.Description)
                : SerializeWithAction(new
                {
                    postId = result.Value.Id,
                    result.Value.ChatSessionId,
                    result.Value.WorkspaceId,
                    result.Value.Title,
                    result.Value.Status,
                    platform,
                    message = "Draft post created and linked to the current chat session."
                },
                    "post_create",
                    "create_post",
                    "completed",
                    "post",
                    result.Value.Id,
                    result.Value.Title,
                    "Draft post created and linked to the current chat session.");
        }

        public async Task<string> UpdatePostAsync(
            [Description("Post id as a GUID string.")] string postId,
            [Description("Optional replacement post body/content text.")] string? content = null,
            [Description("Optional replacement title.")] string? title = null,
            [Description("Optional replacement hashtag text.")] string? hashtag = null,
            [Description("Optional replacement post type.")] string? postType = null,
            [Description("Optional replacement status, for example draft.")] string? status = null,
            [Description("Optional social media GUID.")] string? socialMediaId = null,
            [Description("Optional comma-separated resource GUIDs to replace attached resources.")] string? resourceIds = null,
            [Description("Set true to link the post to the current chat session. Defaults to true.")] bool linkToCurrentChatSession = true)
        {
            Track("update_post");

            if (!Guid.TryParse(postId, out var parsedPostId) || parsedPostId == Guid.Empty)
            {
                RecordAction("post_update", "update_post", "failed", "post", summary: "postId must be a valid GUID.");
                return Serialize(new { error = "postId must be a valid GUID." });
            }

            var existing = await _postRepository.GetByIdAsync(parsedPostId, CancellationToken.None);
            if (existing is null || existing.DeletedAt.HasValue)
            {
                RecordAction("post_update", "update_post", "failed", "post", parsedPostId, summary: "Post not found.");
                return Serialize(new { error = "Post not found." });
            }

            if (existing.UserId != _userId)
            {
                RecordAction("post_update", "update_post", "failed", "post", parsedPostId, existing.Title, "You are not authorized to update this post.");
                return Serialize(new { error = "You are not authorized to update this post." });
            }

            var socialMediaIdResult = ParseOptionalGuid(socialMediaId, "socialMediaId");
            if (socialMediaIdResult.IsFailure)
            {
                RecordAction("post_update", "update_post", "failed", "post", parsedPostId, existing.Title, socialMediaIdResult.Error.Description);
                return Serialize(new { error = socialMediaIdResult.Error.Description });
            }

            var resourceIdsResult = ParseGuidList(resourceIds, "resourceIds");
            if (resourceIdsResult.IsFailure)
            {
                RecordAction("post_update", "update_post", "failed", "post", parsedPostId, existing.Title, resourceIdsResult.Error.Description);
                return Serialize(new { error = resourceIdsResult.Error.Description });
            }

            PostContent? mergedContent = null;
            if (!string.IsNullOrWhiteSpace(content) ||
                !string.IsNullOrWhiteSpace(hashtag) ||
                !string.IsNullOrWhiteSpace(postType) ||
                !string.IsNullOrWhiteSpace(resourceIds))
            {
                mergedContent = new PostContent
                {
                    Content = string.IsNullOrWhiteSpace(content)
                        ? existing.Content?.Content
                        : content.Trim(),
                    Hashtag = string.IsNullOrWhiteSpace(hashtag)
                        ? existing.Content?.Hashtag
                        : hashtag.Trim(),
                    PostType = string.IsNullOrWhiteSpace(postType)
                        ? existing.Content?.PostType
                        : postType.Trim(),
                    ResourceList = string.IsNullOrWhiteSpace(resourceIds)
                        ? existing.Content?.ResourceList
                        : resourceIdsResult.Value.Select(id => id.ToString()).ToList()
                };
            }

            var result = await _mediator.Send(
                new UpdatePostCommand(
                    parsedPostId,
                    _userId,
                    existing.WorkspaceId,
                    linkToCurrentChatSession ? _sessionId : null,
                    socialMediaIdResult.Value,
                    title,
                    mergedContent,
                    status),
                CancellationToken.None);

            return result.IsFailure
                ? SerializeWithAction(
                    new { error = result.Error.Description, code = result.Error.Code },
                    "post_update",
                    "update_post",
                    "failed",
                    "post",
                    parsedPostId,
                    existing.Title,
                    result.Error.Description)
                : SerializeWithAction(new
                {
                    postId = result.Value.Id,
                    result.Value.ChatSessionId,
                    result.Value.WorkspaceId,
                    result.Value.Title,
                    result.Value.Status,
                    message = "Post updated."
                },
                    "post_update",
                    "update_post",
                    "completed",
                    "post",
                    result.Value.Id,
                    result.Value.Title,
                    "Post updated.");
        }

        public async Task<string> CreateScheduleAsync(
            [Description("Schedule name.")] string name,
            [Description("Execution timestamp in ISO 8601 UTC format.")] string executeAtUtc,
            [Description("Timezone like Asia/Ho_Chi_Minh.")] string timezone,
            [Description("Comma-separated social media GUIDs.")] string targetSocialMediaIds,
            [Description("Mode: fixed_content or agentic.")] string? mode = null,
            [Description("Comma-separated post GUIDs for fixed_content schedules.")] string? postIds = null,
            [Description("Optional agent prompt for agentic schedules.")] string? agentPrompt = null,
            [Description("Optional search query template for agentic schedules.")] string? queryTemplate = null,
            [Description("Optional platform preference like facebook or threads.")] string? platformPreference = null,
            [Description("Optional country like VN.")] string? country = null,
            [Description("Optional search language like vi.")] string? searchLanguage = null,
            [Description("Optional search freshness.")] string? freshness = null,
            [Description("Optional result count for web search.")] int count = 5,
            [Description("Optional workspace id. Defaults to current chat workspace.")] string? workspaceId = null)
        {
            Track("create_schedule");

            var resolvedWorkspaceId = ResolveWorkspaceId(workspaceId);
            if (resolvedWorkspaceId.IsFailure)
            {
                RecordAction("schedule_create", "create_schedule", "failed", "schedule", summary: resolvedWorkspaceId.Error.Description);
                return Serialize(new { error = resolvedWorkspaceId.Error.Description });
            }

            if (!DateTime.TryParse(executeAtUtc, out var parsedExecuteAtUtc))
            {
                RecordAction("schedule_create", "create_schedule", "failed", "schedule", summary: "executeAtUtc must be a valid ISO 8601 datetime.");
                return Serialize(new { error = "executeAtUtc must be a valid ISO 8601 datetime." });
            }

            var targetIdsResult = ParseGuidList(targetSocialMediaIds, "targetSocialMediaIds");
            if (targetIdsResult.IsFailure)
            {
                RecordAction("schedule_create", "create_schedule", "failed", "schedule", summary: targetIdsResult.Error.Description);
                return Serialize(new { error = targetIdsResult.Error.Description });
            }

            var normalizedMode = string.IsNullOrWhiteSpace(mode)
                ? PublishingScheduleState.FixedContentMode
                : mode.Trim().ToLowerInvariant();

            if (normalizedMode is "agent" or "agentic_live_content_schedule")
            {
                normalizedMode = PublishingScheduleState.AgenticMode;
            }

            if (normalizedMode == PublishingScheduleState.AgenticMode)
            {
                var result = await _mediator.Send(
                    new CreateAgenticPublishingScheduleCommand(
                        _userId,
                        resolvedWorkspaceId.Value,
                        name,
                        normalizedMode,
                        parsedExecuteAtUtc,
                        timezone,
                        false,
                        platformPreference,
                        agentPrompt,
                        new PublishingScheduleSearchInput(queryTemplate, count, country, searchLanguage, freshness),
                        targetIdsResult.Value.Select(id => new PublishingScheduleTargetInput(id)).ToList()),
                    CancellationToken.None);

                return result.IsFailure
                    ? SerializeWithAction(
                        new { error = result.Error.Description, code = result.Error.Code },
                        "schedule_create",
                        "create_schedule",
                        "failed",
                        "schedule",
                        summary: result.Error.Description)
                    : SerializeWithAction(
                        result.Value,
                        "schedule_create",
                        "create_schedule",
                        "completed",
                        "schedule",
                        result.Value.Id,
                        result.Value.Name,
                        $"Created {result.Value.Mode} schedule.");
            }

            var postIdsResult = ParseGuidList(postIds, "postIds");
            if (postIdsResult.IsFailure || postIdsResult.Value.Count == 0)
            {
                RecordAction("schedule_create", "create_schedule", "failed", "schedule", summary: "postIds is required for fixed_content schedules.");
                return Serialize(new { error = "postIds is required for fixed_content schedules." });
            }

            var fixedResult = await _mediator.Send(
                new CreatePublishingScheduleCommand(
                    _userId,
                    resolvedWorkspaceId.Value,
                    name,
                    normalizedMode,
                    parsedExecuteAtUtc,
                    timezone,
                    false,
                    postIdsResult.Value.Select((id, index) => new PublishingScheduleItemInput("post", id, index + 1, null)).ToList(),
                    targetIdsResult.Value.Select(id => new PublishingScheduleTargetInput(id)).ToList()),
                CancellationToken.None);

            return fixedResult.IsFailure
                ? SerializeWithAction(
                    new { error = fixedResult.Error.Description, code = fixedResult.Error.Code },
                    "schedule_create",
                    "create_schedule",
                    "failed",
                    "schedule",
                    summary: fixedResult.Error.Description)
                : SerializeWithAction(
                    fixedResult.Value,
                    "schedule_create",
                    "create_schedule",
                    "completed",
                    "schedule",
                    fixedResult.Value.Id,
                    fixedResult.Value.Name,
                    $"Created {fixedResult.Value.Mode} schedule.");
        }

        public async Task<string> WebSearchAsync(
            [Description("Search query or query template.")] string query,
            [Description("Optional result count.")] int count = 5,
            [Description("Optional country like VN.")] string? country = null,
            [Description("Optional search language like vi.")] string? language = null,
            [Description("Optional freshness value.")] string? freshness = null)
        {
            Track("web_search");

            var result = await _n8nWorkflowClient.WebSearchAsync(
                new N8nWebSearchRequest(
                    query,
                    Math.Clamp(count, 1, 10),
                    country,
                    language,
                    freshness,
                    null,
                    null),
                CancellationToken.None);

            return result.IsFailure
                ? SerializeWithAction(
                    new { error = result.Error.Description, code = result.Error.Code },
                    "web_search",
                    "web_search",
                    "failed",
                    label: query,
                    summary: result.Error.Description)
                : SerializeWithAction(
                    result.Value,
                    "web_search",
                    "web_search",
                    "completed",
                    label: query,
                    summary: "Web search completed through n8n.");
        }

        private async Task<Post?> FindRecentDraftDuplicateAsync(
            Guid workspaceId,
            string? title,
            PostContent content,
            string? platform,
            CancellationToken cancellationToken)
        {
            var candidates = await _postRepository.GetByUserIdAndChatSessionIdAsync(
                _userId,
                _sessionId,
                null,
                null,
                20,
                cancellationToken);

            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-10);
            var requestedTitle = NormalizeForDedupe(title);
            var requestedContent = NormalizeForDedupe(content.Content);
            var requestedPostType = NormalizeForDedupe(content.PostType) ?? "posts";
            var requestedPlatform = NormalizeForDedupe(platform);

            return candidates.FirstOrDefault(post =>
            {
                if (!string.Equals(post.Status, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (post.WorkspaceId != workspaceId || post.ScheduleGroupId.HasValue || post.ScheduledAtUtc.HasValue)
                {
                    return false;
                }

                if (post.CreatedAt.HasValue && post.CreatedAt.Value < cutoff)
                {
                    return false;
                }

                var existingPostType = NormalizeForDedupe(post.Content?.PostType) ?? "posts";
                if (!string.Equals(existingPostType, requestedPostType, StringComparison.Ordinal))
                {
                    return false;
                }

                var existingPlatform = NormalizeForDedupe(post.Platform);
                if (!string.IsNullOrWhiteSpace(existingPlatform) &&
                    !string.IsNullOrWhiteSpace(requestedPlatform) &&
                    !string.Equals(existingPlatform, requestedPlatform, StringComparison.Ordinal))
                {
                    return false;
                }

                var existingTitle = NormalizeForDedupe(post.Title);
                if (!string.IsNullOrWhiteSpace(requestedTitle) &&
                    string.Equals(existingTitle, requestedTitle, StringComparison.Ordinal))
                {
                    return true;
                }

                var existingContent = NormalizeForDedupe(post.Content?.Content);
                return !string.IsNullOrWhiteSpace(requestedContent) &&
                       string.Equals(existingContent, requestedContent, StringComparison.Ordinal);
            });
        }

        private Result<Guid> ResolveWorkspaceId(string? rawWorkspaceId)
        {
            if (string.IsNullOrWhiteSpace(rawWorkspaceId))
            {
                return Result.Success(_workspaceId);
            }

            if (!Guid.TryParse(rawWorkspaceId, out var workspaceId) || workspaceId == Guid.Empty)
            {
                return Result.Failure<Guid>(new Error("Workspace.InvalidId", "workspaceId must be a valid GUID."));
            }

            return Result.Success(workspaceId);
        }

        private static object MapSocialSummary(UserSocialMediaSummaryResult item)
        {
            return new
            {
                socialMediaId = item.SocialMediaId,
                platform = item.Type,
                username = item.Username,
                displayName = item.DisplayName,
                profilePictureUrl = item.ProfilePictureUrl,
                pageId = item.PageId,
                pageName = item.PageName,
                createdAt = item.CreatedAt,
                updatedAt = item.UpdatedAt
            };
        }

        private static string Serialize(object payload)
        {
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private string SerializeWithAction(
            object payload,
            string type,
            string toolName,
            string status,
            string? entityType = null,
            Guid? entityId = null,
            string? label = null,
            string? summary = null)
        {
            RecordAction(type, toolName, status, entityType, entityId, label, summary);
            return Serialize(payload);
        }

        private static Result<IReadOnlyList<Guid>> ParseGuidList(string? raw, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Result.Success<IReadOnlyList<Guid>>(Array.Empty<Guid>());
            }

            var values = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var ids = new List<Guid>(values.Count);
            foreach (var value in values)
            {
                if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
                {
                    return Result.Failure<IReadOnlyList<Guid>>(
                        new Error("Agent.InvalidGuidList", $"{fieldName} contains an invalid GUID."));
                }

                if (!ids.Contains(parsed))
                {
                    ids.Add(parsed);
                }
            }

            return Result.Success<IReadOnlyList<Guid>>(ids);
        }

        private static Result<Guid?> ParseOptionalGuid(string? raw, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Result.Success<Guid?>(null);
            }

            if (!Guid.TryParse(raw.Trim(), out var parsed) || parsed == Guid.Empty)
            {
                return Result.Failure<Guid?>(
                    new Error("Agent.InvalidGuid", $"{fieldName} must be a valid GUID."));
            }

            return Result.Success<Guid?>(parsed);
        }

        private static string? NormalizeOptionalString(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeForDedupe(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return string.Join(' ', value
                    .Trim()
                    .ToLowerInvariant()
                    .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private void Track(string toolName)
        {
            _invokedToolNames.Add(toolName);
        }

        private void RecordAction(
            string type,
            string toolName,
            string status,
            string? entityType = null,
            Guid? entityId = null,
            string? label = null,
            string? summary = null)
        {
            _actions.Add(new AgentActionResponse(
                type,
                toolName,
                status,
                entityType,
                entityId,
                label,
                summary,
                DateTime.UtcNow));
        }
    }
}
