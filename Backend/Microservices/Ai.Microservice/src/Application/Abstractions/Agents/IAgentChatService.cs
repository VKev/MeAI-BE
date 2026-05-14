using Application.Agents.Models;
using Application.PublishingSchedules.Models;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Agents;

public interface IAgentChatService
{
    Task<Result<AgentChatCompletionResult>> GenerateReplyAsync(
        AgentChatRequest request,
        CancellationToken cancellationToken);
}

public sealed record AgentChatRequest(
    Guid UserId,
    Guid SessionId,
    Guid WorkspaceId,
    string Message,
    AgentImageOptions? ImageOptions = null,
    AgentVideoOptions? VideoOptions = null,
    AgentScheduleOptions? ScheduleOptions = null,
    Guid? AssistantChatId = null);

public sealed record AgentImageOptions(
    string? Model = null,
    string? AspectRatio = null,
    string? Resolution = null,
    int? NumberOfVariances = null,
    IReadOnlyList<AgentSocialTarget>? SocialTargets = null);

public sealed record AgentVideoOptions(
    string? Model = null,
    string? AspectRatio = null,
    int? Seeds = null,
    bool? EnableTranslation = null,
    string? Watermark = null,
    IReadOnlyList<Guid>? ResourceIds = null);

public sealed record AgentSocialTarget(
    string Platform,
    string Type,
    string Ratio);

public sealed record AgentScheduleOptions(
    DateTime ExecuteAtUtc,
    string? Timezone,
    int? MaxContentLength,
    IReadOnlyList<PublishingScheduleTargetInput>? Targets);

public sealed record AgentChatCompletionResult(
    string Content,
    string? Model,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<AgentActionResponse>? Actions = null,
    string? Action = null,
    string? ValidationError = null,
    string? RevisedPrompt = null,
    Guid? PostId = null,
    Guid? ScheduleId = null,
    Guid? ChatId = null,
    Guid? CorrelationId = null,
    string? RetrievalMode = null,
    IReadOnlyList<string>? SourceUrls = null,
    IReadOnlyList<Guid>? ImportedResourceIds = null,
    Guid? PostBuilderId = null,
    IReadOnlyList<Guid>? PostIds = null);
