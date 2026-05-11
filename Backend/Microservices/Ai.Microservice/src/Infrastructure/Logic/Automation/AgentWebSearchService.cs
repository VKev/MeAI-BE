using Application.Abstractions.Automation;
using Application.Abstractions.Search;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Automation;

public sealed class AgentWebSearchService : IAgentWebSearchService
{
    private readonly IWebSearchClient _webSearchClient;
    private readonly IWebSearchEnrichmentService _webSearchEnrichmentService;
    private readonly ILogger<AgentWebSearchService> _logger;

    public AgentWebSearchService(
        IWebSearchClient webSearchClient,
        IWebSearchEnrichmentService webSearchEnrichmentService,
        ILogger<AgentWebSearchService> logger)
    {
        _webSearchClient = webSearchClient;
        _webSearchEnrichmentService = webSearchEnrichmentService;
        _logger = logger;
    }

    public async Task<Result<AgentWebSearchResponse>> SearchAsync(
        AgentWebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Result.Failure<AgentWebSearchResponse>(
                new Error("AgentWebSearch.InvalidQuery", "Web search query is required."));
        }

        try
        {
            var hits = await _webSearchClient.SearchAsync(
                request.Query,
                request.Count,
                cancellationToken);

            var response = new AgentWebSearchResponse(
                request.Query,
                DateTime.UtcNow,
                hits.Select(hit => new AgentWebSearchResultItem(
                    hit.Title,
                    hit.Url,
                    hit.Snippet,
                    "search")).ToList(),
                null,
                []);

            var enriched = await _webSearchEnrichmentService.EnrichAsync(
                response,
                request.UserId,
                request.WorkspaceId,
                request.OriginChatSessionId,
                request.OriginChatId,
                cancellationToken);

            return Result.Success(enriched);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local agent web search failed for query '{Query}'", request.Query);
            return Result.Failure<AgentWebSearchResponse>(
                new Error("AgentWebSearch.SearchFailed", ex.Message));
        }
    }
}
