using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Automation;

public interface IN8nWorkflowClient
{
    Task<Result<N8nScheduledAgentJobAck>> RegisterScheduledAgentJobAsync(
        N8nScheduledAgentJobRequest request,
        CancellationToken cancellationToken);

    Task<Result<N8nWebSearchResponse>> WebSearchAsync(
        N8nWebSearchRequest request,
        CancellationToken cancellationToken);
}

public sealed record N8nScheduledAgentJobRequest(
    Guid JobId,
    Guid ScheduleId,
    Guid UserId,
    Guid WorkspaceId,
    DateTime ExecuteAtUtc,
    string Timezone,
    N8nWebSearchRequest Search);

public sealed record N8nScheduledAgentJobAck(
    string? ExecutionId,
    DateTime AcceptedAtUtc);

public sealed record N8nWebSearchRequest(
    string QueryTemplate,
    int Count,
    string? Country,
    string? SearchLanguage,
    string? Freshness,
    string? Timezone = null,
    DateTime? ExecuteAtUtc = null);

public sealed record N8nWebSearchResponse(
    string Query,
    DateTime RetrievedAtUtc,
    IReadOnlyList<N8nWebSearchResultItem> Results,
    string? LlmContext);

public sealed record N8nWebSearchResultItem(
    string? Title,
    string? Url,
    string? Description,
    string? Source);
