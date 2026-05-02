using System.Text;
using Application.Abstractions.Rag;
using Application.Abstractions.SocialMedias;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Queries;

public sealed record QueryAccountRecommendationsQuery(
    Guid UserId,
    Guid SocialMediaId,
    string Query,
    int? TopK = null) : IRequest<Result<AccountRecommendationsAnswer>>;

public sealed record AccountRecommendationsAnswer(
    string Answer,
    string DocumentIdPrefix,
    IReadOnlyList<RecommendationReference> References);

public sealed record RecommendationReference(
    string DocumentId,
    string? PostId,
    string? ImageUrl,
    string? Caption,
    string Source,
    double Score);

public sealed class QueryAccountRecommendationsQueryHandler
    : IRequestHandler<QueryAccountRecommendationsQuery, Result<AccountRecommendationsAnswer>>
{
    private const int DefaultTopK = 6;
    private const int RrfK = 60;
    private const int MaxImagesToLlm = 4;

    private const string SystemPrompt =
        "You are a social-media content strategist with deep knowledge of the user's account. " +
        "You receive (a) a text context block describing the user's past posts and engagement, " +
        "and (b) a few reference images from past posts visually retrieved for the user's question. " +
        "Use the images as visual evidence — describe and reference them when relevant. " +
        "Answer in the same language as the user's question. Be concrete, cite which post(s) " +
        "you're drawing from when possible, and propose actionable next steps.";

    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IRagClient _ragClient;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly ILogger<QueryAccountRecommendationsQueryHandler> _logger;

    public QueryAccountRecommendationsQueryHandler(
        IUserSocialMediaService userSocialMediaService,
        IRagClient ragClient,
        IMultimodalLlmClient multimodalLlm,
        ILogger<QueryAccountRecommendationsQueryHandler> logger)
    {
        _userSocialMediaService = userSocialMediaService;
        _ragClient = ragClient;
        _multimodalLlm = multimodalLlm;
        _logger = logger;
    }

    public async Task<Result<AccountRecommendationsAnswer>> Handle(
        QueryAccountRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Result.Failure<AccountRecommendationsAnswer>(
                new Error("Recommendations.EmptyQuery", "Query text is required."));
        }

        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<AccountRecommendationsAnswer>(socialMediaResult.Error);
        }

        var socialMedia = socialMediaResult.Value.FirstOrDefault();
        if (socialMedia == null)
        {
            return Result.Failure<AccountRecommendationsAnswer>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        var platform = (socialMedia.Type ?? string.Empty).ToLowerInvariant();
        var prefix = $"{platform}:{request.SocialMediaId:N}:";
        var topK = request.TopK ?? DefaultTopK;

        var rag = await _ragClient.MultimodalQueryAsync(
            new RagMultimodalQueryRequest(
                Query: request.Query,
                DocumentIdPrefix: prefix,
                TopK: topK,
                Modes: new[] { "text", "visual" }),
            cancellationToken);

        if (rag.VisualError != null)
        {
            _logger.LogWarning(
                "Visual retrieval failed for socialMediaId={SocialMediaId}: {Error}",
                request.SocialMediaId,
                rag.VisualError);
        }

        // Reciprocal Rank Fusion: collapse text matched-doc-ids and visual hits
        // into a single ranked list of distinct postIds. Visual hits carry the
        // image_url payload we'll feed back to the multimodal LLM.
        var fusedReferences = FuseReferences(rag, prefix);

        var imageRefsForLlm = fusedReferences
            .Where(r => !string.IsNullOrWhiteSpace(r.ImageUrl))
            .Select(r => r.ImageUrl!)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxImagesToLlm)
            .ToList();

        var userTextBuilder = new StringBuilder();
        userTextBuilder.AppendLine($"User question: {request.Query}");
        userTextBuilder.AppendLine();
        if (!string.IsNullOrWhiteSpace(rag.Text?.Context))
        {
            userTextBuilder.AppendLine("=== Retrieved context from past posts (text + analytics) ===");
            userTextBuilder.AppendLine(rag.Text!.Context);
            userTextBuilder.AppendLine();
        }
        if (fusedReferences.Count > 0)
        {
            userTextBuilder.AppendLine("=== Top retrieved post references ===");
            for (var i = 0; i < fusedReferences.Count; i++)
            {
                var r = fusedReferences[i];
                userTextBuilder.AppendLine(
                    $"[{i + 1}] postId={r.PostId} score={r.Score:F4} source={r.Source} caption=\"{Truncate(r.Caption, 160)}\"");
            }
            userTextBuilder.AppendLine();
        }
        if (imageRefsForLlm.Count > 0)
        {
            userTextBuilder.AppendLine($"The next {imageRefsForLlm.Count} attached image(s) are the actual reference images from those posts, in the same order. Use them as visual evidence when answering.");
        }

        string answer;
        try
        {
            answer = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: SystemPrompt,
                    UserText: userTextBuilder.ToString(),
                    ReferenceImageUrls: imageRefsForLlm),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multimodal LLM call failed for socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<AccountRecommendationsAnswer>(
                new Error("Recommendations.LlmFailed", $"Answer generation failed: {ex.Message}"));
        }

        return Result.Success(new AccountRecommendationsAnswer(
            Answer: answer,
            DocumentIdPrefix: prefix,
            References: fusedReferences));
    }

    private static List<RecommendationReference> FuseReferences(
        RagMultimodalQueryResponse rag,
        string prefix)
    {
        // Reciprocal Rank Fusion: score(doc) = Σ 1/(K + rank_i).
        // text matched-ids contribute by rank-only (we don't have per-id scores back).
        // visual hits contribute by their qdrant rank.
        var rrfScores = new Dictionary<string, double>(StringComparer.Ordinal);
        var byPostId = new Dictionary<string, RecommendationReference>(StringComparer.Ordinal);

        // Text leg
        var textIds = rag.Text?.MatchedDocumentIds ?? Array.Empty<string>();
        for (var i = 0; i < textIds.Count; i++)
        {
            var docId = textIds[i];
            var postId = ExtractPostId(docId, prefix);
            var key = postId ?? docId;
            rrfScores[key] = rrfScores.GetValueOrDefault(key) + 1.0 / (RrfK + i + 1);
            if (!byPostId.ContainsKey(key))
            {
                byPostId[key] = new RecommendationReference(
                    DocumentId: docId,
                    PostId: postId,
                    ImageUrl: null,
                    Caption: null,
                    Source: "text",
                    Score: 0d);
            }
        }

        // Visual leg
        var visualHits = rag.Visual ?? Array.Empty<RagVisualHit>();
        for (var i = 0; i < visualHits.Count; i++)
        {
            var hit = visualHits[i];
            var key = hit.PostId ?? hit.DocumentId ?? $"visual:{i}";
            rrfScores[key] = rrfScores.GetValueOrDefault(key) + 1.0 / (RrfK + i + 1);

            if (byPostId.TryGetValue(key, out var existing))
            {
                byPostId[key] = existing with
                {
                    ImageUrl = existing.ImageUrl ?? hit.ImageUrl,
                    Caption = existing.Caption ?? hit.Caption,
                    Source = "text+visual",
                };
            }
            else
            {
                byPostId[key] = new RecommendationReference(
                    DocumentId: hit.DocumentId ?? key,
                    PostId: hit.PostId,
                    ImageUrl: hit.ImageUrl,
                    Caption: hit.Caption,
                    Source: "visual",
                    Score: 0d);
            }
        }

        return byPostId
            .Select(kvp => kvp.Value with { Score = rrfScores[kvp.Key] })
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static string? ExtractPostId(string documentId, string prefix)
    {
        if (string.IsNullOrEmpty(documentId) || !documentId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }
        var tail = documentId.Substring(prefix.Length);
        var sep = tail.IndexOf(':');
        return sep < 0 ? tail : tail.Substring(0, sep);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max) + "…";
    }
}
