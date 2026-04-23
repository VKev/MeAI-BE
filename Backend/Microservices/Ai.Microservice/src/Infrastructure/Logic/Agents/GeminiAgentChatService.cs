using System.ComponentModel;
using System.Text.Json;
using Application.Abstractions.Agents;
using Application.Abstractions.ApiCredentials;
using Application.Abstractions.Automation;
using Application.Abstractions.Configs;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Workspaces;
using Application.Agents;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Commands;
using Application.PublishingSchedules.Models;
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
                tools.GetInvokedToolNames()));
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
        private readonly Guid _workspaceId;
        private readonly IUserWorkspaceService _userWorkspaceService;
        private readonly IUserSocialMediaService _userSocialMediaService;
        private readonly IPostRepository _postRepository;
        private readonly IPublishingScheduleRepository _publishingScheduleRepository;
        private readonly PublishingScheduleResponseBuilder _publishingScheduleResponseBuilder;
        private readonly IN8nWorkflowClient _n8nWorkflowClient;
        private readonly IMediator _mediator;
        private readonly List<string> _invokedToolNames = new();

        public AgentToolbox(
            Guid userId,
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

        public async Task<string> GetUserWorkspacesAsync()
        {
            Track("get_user_workspaces");

            var result = await _userWorkspaceService.GetWorkspacesAsync(_userId, CancellationToken.None);
            if (result.IsFailure)
            {
                return Serialize(new { error = result.Error.Description });
            }

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
                return Serialize(new { error = result.Error.Description });
            }

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
                return Serialize(new { error = result.Error.Description });
            }

            return Serialize(result.Value.Select(MapSocialSummary));
        }

        public async Task<string> GetPostsAsync(
            [Description("Optional workspace id. If omitted, the current chat session workspace is used.")] string? workspaceId = null,
            [Description("Optional post status filter like draft, scheduled, processing, or failed.")] string? status = null,
            [Description("Maximum number of posts to return.")] int limit = 20)
        {
            Track("get_posts");

            limit = Math.Clamp(limit, 1, 50);

            IReadOnlyList<Domain.Entities.Post> posts;
            if (string.IsNullOrWhiteSpace(workspaceId))
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

            return Serialize(filteredPosts.Select(post => new
            {
                postId = post.Id,
                workspaceId = post.WorkspaceId,
                title = post.Title,
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

                return Serialize(new
                {
                    utc = utcNow,
                    timezone = zone.Id,
                    localTime
                });
            }
            catch (TimeZoneNotFoundException)
            {
                return Serialize(new { error = $"Timezone '{timezone}' was not found." });
            }
            catch (InvalidTimeZoneException)
            {
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
                return Serialize(new { error = "scheduleId must be a valid GUID." });
            }

            var schedule = await _publishingScheduleRepository.GetByIdAsync(parsedScheduleId, CancellationToken.None);
            if (schedule is null || schedule.DeletedAt.HasValue || schedule.UserId != _userId)
            {
                return Serialize(new { error = "Publishing schedule not found." });
            }

            var response = await _publishingScheduleResponseBuilder.BuildAsync(schedule, CancellationToken.None);
            return Serialize(response);
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
                return Serialize(new { error = resolvedWorkspaceId.Error.Description });
            }

            if (!DateTime.TryParse(executeAtUtc, out var parsedExecuteAtUtc))
            {
                return Serialize(new { error = "executeAtUtc must be a valid ISO 8601 datetime." });
            }

            var targetIdsResult = ParseGuidList(targetSocialMediaIds, "targetSocialMediaIds");
            if (targetIdsResult.IsFailure)
            {
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
                    ? Serialize(new { error = result.Error.Description, code = result.Error.Code })
                    : Serialize(result.Value);
            }

            var postIdsResult = ParseGuidList(postIds, "postIds");
            if (postIdsResult.IsFailure || postIdsResult.Value.Count == 0)
            {
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
                ? Serialize(new { error = fixedResult.Error.Description, code = fixedResult.Error.Code })
                : Serialize(fixedResult.Value);
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
                ? Serialize(new { error = result.Error.Description, code = result.Error.Code })
                : Serialize(result.Value);
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

        private void Track(string toolName)
        {
            _invokedToolNames.Add(toolName);
        }
    }
}
