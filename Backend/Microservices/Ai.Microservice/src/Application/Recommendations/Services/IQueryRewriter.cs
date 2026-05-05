using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Services;

/// <summary>
/// Single-LLM-call query rewriter. The user's raw prompt (and optional page profile
/// hints) get rewritten into a richer set of text/visual queries plus a typed intent
/// classification. Outputs feed every retrieval + rerank query in the recommendation
/// pipeline.
///
/// Why this exists: raw <c>userPrompt</c> values are often vague imperatives
/// (e.g. <c>"make content about news camera nowaday"</c>). They embed poorly into
/// the cosine space because they lack the key terms ("DJI Osmo", "mirrorless body",
/// "sensor") that retrievable docs actually contain. A <see cref="QueryRewriter"/>
/// LLM call costs ~$0.0005 and lifts retrieval recall ~30-50% (HyDE-class).
///
/// The rewriter is called once per request — typically by the orchestrating consumer
/// (DraftPostGenerationConsumer / RecommendPostGenerationConsumer / direct /query
/// controller) which then threads the result through MediatR via
/// <c>QueryAccountRecommendationsQuery.PrecomputedRewrite</c> so the handler doesn't
/// re-issue the LLM call.
/// </summary>
public interface IQueryRewriter
{
    Task<Result<QueryRewriteResult>> RewriteAsync(
        QueryRewriteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs for the rewriter. <paramref name="UserPrompt"/> is mandatory — everything
/// else is hints to ground the rewrite.
/// </summary>
public sealed record QueryRewriteRequest(
    string UserPrompt,
    string? PageProfileSnippet = null,   // page About / category / niche; helps rewriter
                                          // pick relevant key-terms in the page's voice
    string? Platform = null,             // "facebook" / "tiktok" / etc.
    string? Style = null,                // "creative" / "branded" / "marketing"
    string? PageLanguageHint = null);    // optional ISO-639-1 hint; rewriter detects from
                                          // PageProfileSnippet if not provided

/// <summary>
/// Output of one rewrite call. Used to populate every retrieval + rerank query slot.
/// </summary>
public sealed record QueryRewriteResult(
    /// <summary>ISO-639-1 of the page's primary content language. Used to localize
    /// the hardcoded literals (R4 platform-pinned, R6 profile, R7 style-design) so a
    /// Vietnamese page embeds Vietnamese literals against its Vietnamese docs.</summary>
    string Language,
    /// <summary>Typed intent: "viral" | "informational" | "sales" | "story" |
    /// "engagement" | "design" | "algorithm". Routes the semantic-knowledge fan-out
    /// (R5) to the most relevant knowledge namespaces.</summary>
    string Intent,
    /// <summary>The primary rewritten retrieval query — descriptive, key-term-rich,
    /// in <see cref="Language"/>. Used for R1/R3 (multimodal text+video legs) and
    /// the K1-K3 reranker queries.</summary>
    string PrimaryQuery,
    /// <summary>2-3 alternative angles for the same intent. Used in R5 fan-out so we
    /// don't miss relevant knowledge chunks under embedding-space drift.</summary>
    IReadOnlyList<string> AltQueries,
    /// <summary>Visually-descriptive query for image rerank (R2 multimodal visual leg
    /// + K4 image candidate pool). Stays in English because Jina-m0 / Gemini multimodal
    /// embeddings are best anchored on visual nouns ("modern mirrorless camera, sleek
    /// body, professional photography equipment").</summary>
    string VisualQuery,
    /// <summary>Salient noun phrases distilled from the rewrite. Appended to rerank
    /// queries to give the cross-encoder more anchors (e.g. "DJI Osmo", "mirrorless").</summary>
    IReadOnlyList<string> KeyTerms);
