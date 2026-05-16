using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Automation;

public interface IAgentWebSearchService
{
    Task<Result<AgentWebSearchResponse>> SearchAsync(
        AgentWebSearchRequest request,
        CancellationToken cancellationToken);
}

public sealed record AgentWebSearchRequest(
    string Query,
    int Count,
    string? Country = null,
    string? SearchLanguage = null,
    string? Freshness = null,
    Guid? UserId = null,
    Guid? WorkspaceId = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);

public sealed record AgentWebSearchResponse(
    string Query,
    DateTime RetrievedAtUtc,
    IReadOnlyList<AgentWebSearchResultItem> Results,
    string? LlmContext,
    IReadOnlyList<ImportedResourceItem>? ImportedResources = null);

public sealed record AgentWebSearchResultItem(
    string? Title,
    string? Url,
    string? Description,
    string? Source,
    string? PageTitle = null,
    string? PageContent = null,
    IReadOnlyList<string>? MediaUrls = null);

public sealed record ImportedResourceItem(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    string SourceUrl,
    string? SourcePageUrl);
