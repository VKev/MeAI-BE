namespace Application.Abstractions.Automation;

public interface IWebSearchEnrichmentService
{
    Task<N8nWebSearchResponse> EnrichAsync(
        N8nWebSearchResponse response,
        Guid? userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        CancellationToken cancellationToken);

    Task<N8nWebSearchResponse> EnrichUrlsAsync(
        IReadOnlyList<string> urls,
        string query,
        Guid? userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        CancellationToken cancellationToken);
}
