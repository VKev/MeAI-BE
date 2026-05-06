using System.Text;
using System.Text.Json;
using Application.Abstractions.Rag;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Services;

/// <summary>
/// Single-LLM-call query rewriter — see <see cref="IQueryRewriter"/>.
///
/// Implementation detail: we use the existing <see cref="IMultimodalLlmClient"/>
/// (gpt-4o-mini) with web-search disabled. The prompt asks for strictly-shaped JSON
/// and we parse it; if parsing fails we fall back to a degenerate result that
/// preserves the user's original prompt verbatim so the pipeline still works
/// (just without the rewrite quality lift).
/// </summary>
public sealed class QueryRewriter : IQueryRewriter
{
    /// <summary>
    /// System prompt for the rewriter LLM. Constraints:
    ///   * Output is JSON only — no markdown fence, no preamble.
    ///   * <c>language</c> uses ISO-639-1 and is detected from page profile when present.
    ///   * <c>primary_query</c> stays in the page's language; <c>visual_query</c> is
    ///     always English because vision models embed visual nouns better in English.
    /// </summary>
    private const string SystemPrompt =
        "You are a query rewriter for a retrieval-augmented social-media content " +
        "recommendation system. Your job: turn the user's raw, often-imperative prompt " +
        "(e.g. \"make content about X\") into a structured set of retrieval queries that " +
        "will embed well into a cosine vector space.\n\n" +
        "INPUTS YOU SEE:\n" +
        "  - user_prompt: the raw prompt\n" +
        "  - page_profile_snippet: (optional) About / category / niche of the social account\n" +
        "  - platform: (optional) which social platform the post is for\n" +
        "  - style: (optional) creative / branded / marketing\n\n" +
        "OUTPUT (strict JSON, no Markdown, no preamble):\n" +
        "{\n" +
        "  \"language\":      \"<ISO-639-1, e.g. en | vi | ja | ko | th>\",\n" +
        "  \"intent\":        \"<one of: viral | informational | sales | story | engagement | design | algorithm>\",\n" +
        "  \"primary_query\": \"<descriptive, key-term-rich rewrite of user_prompt, in the detected language>\",\n" +
        "  \"alt_queries\":   [\"<alternative angle 1>\", \"<alternative angle 2>\"],\n" +
        "  \"visual_query\":  \"<English visually-descriptive query — what the IMAGE for this post looks like>\",\n" +
        "  \"key_terms\":     [\"<noun phrase>\", \"<noun phrase>\", \"<noun phrase>\"]\n" +
        "}\n\n" +
        "RULES:\n" +
        "  * language: detect from page_profile_snippet first, then user_prompt. Default \"en\".\n" +
        "  * intent: pick the one that best matches the user_prompt's GOAL.\n" +
        "  * primary_query: same language as detected; expand vague terms with concrete\n" +
        "    nouns (e.g. \"camera\" → \"mirrorless camera\", \"DJI Osmo Pocket\"). Aim for\n" +
        "    ~10-25 words.\n" +
        "  * alt_queries: 2-3 alternative angles in the SAME language. Cover different\n" +
        "    facets (e.g. one focused on the product, one on the use case, one on the\n" +
        "    audience benefit).\n" +
        "  * visual_query: ALWAYS English. Describe the IMAGE you'd expect to see in a\n" +
        "    post about this topic. Use concrete visual nouns (\"sleek black body\",\n" +
        "    \"product photography\", \"natural lighting\"). 8-20 words.\n" +
        "  * key_terms: 3-6 distilled noun phrases (English or detected language —\n" +
        "    pick whichever serves rerank better; English usually wins).\n\n" +
        "Output JSON ONLY. No backticks. No \"Here is the JSON:\" preamble.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IMultimodalLlmClient _llm;
    private readonly ILogger<QueryRewriter> _logger;

    public QueryRewriter(IMultimodalLlmClient llm, ILogger<QueryRewriter> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<Result<QueryRewriteResult>> RewriteAsync(
        QueryRewriteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            return Result.Failure<QueryRewriteResult>(
                new Error("QueryRewriter.EmptyPrompt", "user_prompt is required."));
        }

        var userText = BuildUserText(request);
        try
        {
            var llmResult = await _llm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: SystemPrompt,
                    UserText: userText,
                    ReferenceImageUrls: null),
                cancellationToken);

            var raw = (llmResult.Answer ?? string.Empty).Trim();
            var rewrite = ParseLlmJson(raw, request.UserPrompt);
            _logger.LogInformation(
                "QueryRewriter: lang={Language} intent={Intent} primary={PrimaryLen}ch alts={AltCount} visual={VisualLen}ch keyTerms={KeyTermCount}",
                rewrite.Language, rewrite.Intent,
                rewrite.PrimaryQuery.Length, rewrite.AltQueries.Count,
                rewrite.VisualQuery.Length, rewrite.KeyTerms.Count);
            return Result.Success(rewrite);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "QueryRewriter LLM call failed — falling back to identity rewrite");
            return Result.Success(IdentityFallback(request.UserPrompt));
        }
    }

    private static string BuildUserText(QueryRewriteRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"user_prompt: {request.UserPrompt}");
        if (!string.IsNullOrWhiteSpace(request.PageProfileSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("page_profile_snippet:");
            sb.AppendLine(Truncate(request.PageProfileSnippet, 2000));
        }
        if (!string.IsNullOrWhiteSpace(request.Platform))
        {
            sb.AppendLine();
            sb.AppendLine($"platform: {request.Platform}");
        }
        if (!string.IsNullOrWhiteSpace(request.Style))
        {
            sb.AppendLine($"style: {request.Style}");
        }
        if (!string.IsNullOrWhiteSpace(request.PageLanguageHint))
        {
            sb.AppendLine($"page_language_hint: {request.PageLanguageHint}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse the LLM output. Tolerant of: leading/trailing whitespace, markdown fences
    /// ('```json'), trailing commentary. Falls back to identity rewrite on any error.
    /// </summary>
    private QueryRewriteResult ParseLlmJson(string raw, string originalPrompt)
    {
        var cleaned = StripJsonFence(raw);
        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            string language = ReadString(root, "language") ?? "en";
            string intent = ReadString(root, "intent") ?? "informational";
            string primary = ReadString(root, "primary_query") ?? originalPrompt;
            var alts = ReadStringArray(root, "alt_queries");
            string visual = ReadString(root, "visual_query") ?? originalPrompt;
            var keyTerms = ReadStringArray(root, "key_terms");

            return new QueryRewriteResult(
                Language: language.Trim().ToLowerInvariant(),
                Intent: intent.Trim().ToLowerInvariant(),
                PrimaryQuery: primary.Trim(),
                AltQueries: alts.Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
                VisualQuery: visual.Trim(),
                KeyTerms: keyTerms.Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "QueryRewriter: LLM returned non-JSON output (length={Len}) — falling back to identity",
                raw.Length);
            return IdentityFallback(originalPrompt);
        }
    }

    private static string StripJsonFence(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var newline = s.IndexOf('\n');
            if (newline > 0) s = s[(newline + 1)..];
            var end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 0) s = s[..end];
            s = s.Trim();
        }
        return s;
    }

    private static string? ReadString(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString();
        }
        return null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            return v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .ToList();
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// Degenerate "no rewrite" result. Returned when the LLM fails or its output is
    /// unparseable. The pipeline still runs — just without the embedding-space lift
    /// that the rewriter would have provided.
    /// </summary>
    private static QueryRewriteResult IdentityFallback(string originalPrompt)
    {
        return new QueryRewriteResult(
            Language: "en",
            Intent: "informational",
            PrimaryQuery: originalPrompt,
            AltQueries: Array.Empty<string>(),
            VisualQuery: originalPrompt,
            KeyTerms: Array.Empty<string>());
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
