namespace Application.Abstractions.Search;

/// <summary>
/// Minimal web-search abstraction. Currently has a single Brave-backed implementation
/// in Infrastructure; abstracted so the answer-generation LLM tool loop doesn't depend
/// on the specific provider.
/// </summary>
public interface IWebSearchClient
{
    Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken);
}

public sealed record WebSearchHit(
    string Url,
    string Title,
    string? Snippet,
    string? Age);
