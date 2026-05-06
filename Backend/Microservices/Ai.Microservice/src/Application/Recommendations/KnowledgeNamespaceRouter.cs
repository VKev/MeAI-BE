namespace Application.Recommendations;

/// <summary>
/// Maps a typed user intent (from <see cref="Services.QueryRewriteResult.Intent"/>) to
/// the most relevant <c>knowledge:*</c> doc-id prefixes for the broad-semantic-knowledge
/// fan-out (R5 in the query map).
///
/// Without this gating, R5 hits the bare <c>knowledge:</c> prefix — top-3 chunks across
/// all 8 namespaces. Cosine score luck decides whether the LLM ends up with a viral-hook
/// chunk or a design rule. With gating, we route to the namespaces most likely to
/// contain the answer for the typed intent.
///
/// The fallback is always the bare <c>knowledge:</c> prefix so we never miss content
/// when intent classification is uncertain.
/// </summary>
public static class KnowledgeNamespaceRouter
{
    /// <summary>
    /// Returns 1-3 namespace prefixes appropriate for the given intent. Always includes
    /// at least one prefix; when intent is unknown, returns a single broad
    /// <c>knowledge:</c> fallback so the existing behavior is preserved.
    /// </summary>
    public static IReadOnlyList<string> NamespacesFor(string? intent)
    {
        var key = (intent ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "viral" => new[]
            {
                "knowledge:viral-hooks:",
                "knowledge:content-formulas:",
                "knowledge:engagement-triggers:",
            },
            "informational" => new[]
            {
                "knowledge:content-formulas:",
                "knowledge:viral-hooks:",
                "knowledge:engagement-triggers:",
            },
            "sales" => new[]
            {
                "knowledge:content-formulas:",
                "knowledge:engagement-triggers:",
            },
            "story" => new[]
            {
                "knowledge:content-formulas:",
                "knowledge:viral-hooks:",
            },
            "engagement" => new[]
            {
                "knowledge:engagement-triggers:",
                "knowledge:content-formulas:",
                "knowledge:viral-hooks:",
            },
            "design" => new[]
            {
                "knowledge:visual-design:",
                "knowledge:content-formulas:",
            },
            "algorithm" => new[]
            {
                "knowledge:platform-algorithm-signals:",
                "knowledge:content-formulas:",
            },
            // Unknown intent → fall back to the original broad search across all
            // knowledge. The cost is the same as today; we just lose the precision lift.
            _ => new[] { "knowledge:" },
        };
    }
}
