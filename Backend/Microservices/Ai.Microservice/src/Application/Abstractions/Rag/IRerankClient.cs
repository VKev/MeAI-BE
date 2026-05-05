namespace Application.Abstractions.Rag;

/// <summary>
/// Cross-encoder reranker abstraction. Given a query + a list of multimodal candidate
/// documents (each can have text, an image URL, or both), returns a relevance score
/// per candidate (higher = more relevant). Used after first-pass retrieval to refine
/// which image references the draft-generation pipeline forwards to the image-brief
/// LLM and the image-gen model.
///
/// The reranker only SCORES — caller decides selection (top-K, threshold, threshold+cap).
/// </summary>
public interface IRerankClient
{
    /// <summary>
    /// Score every candidate against the query. Returned list contains one
    /// <see cref="RerankResult"/> per input document, sorted by descending relevance.
    /// Returns an empty list on failure or when reranking is disabled — callers
    /// must fall back to their original ordering rather than aborting.
    /// </summary>
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<RerankDocument> documents,
        CancellationToken cancellationToken);
}

/// <summary>
/// One multimodal candidate. Pass <see cref="Text"/> alone for text-only mode,
/// or both <see cref="Text"/> + <see cref="ImageUrl"/> for true multimodal rerank
/// (the reranker scores against both the textual description AND the image content).
/// </summary>
public sealed record RerankDocument(
    string? Text = null,
    string? ImageUrl = null);

/// <summary>
/// One scored candidate. <see cref="Index"/> is the position in the original
/// <c>documents</c> list (so the caller can map back to whatever object the text
/// described — e.g. an image URL).
/// </summary>
public sealed record RerankResult(int Index, double Score);
