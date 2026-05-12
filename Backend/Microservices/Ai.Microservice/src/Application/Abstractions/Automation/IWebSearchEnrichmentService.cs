namespace Application.Abstractions.Automation;

public interface IWebSearchEnrichmentService
{
    Task<AgentWebSearchResponse> EnrichAsync(
        AgentWebSearchResponse response,
        Guid? userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        CancellationToken cancellationToken);

    Task<AgentWebSearchResponse> EnrichUrlsAsync(
        IReadOnlyList<string> urls,
        string query,
        Guid? userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        CancellationToken cancellationToken);
}
