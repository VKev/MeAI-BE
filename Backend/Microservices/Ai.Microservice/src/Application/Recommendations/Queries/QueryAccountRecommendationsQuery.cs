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
    IReadOnlyList<RecommendationReference> References,
    IReadOnlyList<WebSource>? WebSources = null,
    /// <summary>
    /// Verbatim page profile (name / about / category / website / email / phone /
    /// location / bio / company overview), as RAG'd from the per-account profile
    /// doc at ingest time. Forwarded so downstream caption + image-brief LLMs can
    /// quote contact info EXACTLY rather than paraphrasing it from the recommendation
    /// summary (paraphrasing was causing them to hallucinate canonical-looking URLs).
    /// </summary>
    string? PageProfileText = null);

public sealed record RecommendationReference(
    string DocumentId,
    string? PostId,
    string? ImageUrl,
    string? Caption,
    string Source,
    double Score,
    string? VideoSegmentTime = null,
    string? VideoTranscript = null,
    /// <summary>
    /// S3-mirror URL for ImageUrl, fetchable by OpenAI / OpenRouter (the original
    /// FB CDN URL is not). Use this when handing image refs to multimodal LLMs.
    /// </summary>
    string? MirroredImageUrl = null,
    /// <summary>
    /// S3 URLs of the frame(s) from this post's matched video segments — one URL
    /// per matched segment, taken from the highest-scoring sampled frame within
    /// each segment. Populated only when the underlying RAG hit was a video segment
    /// AND the new frame-level Qdrant store ingested per-frame vectors. Image-rerank
    /// surfaces these alongside the post's thumbnail so image-gen can use the
    /// actual visually-relevant frame as a reference.
    /// </summary>
    IReadOnlyList<string>? VideoFrameUrls = null);

public sealed class QueryAccountRecommendationsQueryHandler
    : IRequestHandler<QueryAccountRecommendationsQuery, Result<AccountRecommendationsAnswer>>
{
    private const int DefaultTopK = 6;
    private const int RrfK = 60;
    private const int MaxImagesToLlm = 4;

    /// <summary>
    /// Threshold for the text-rerank pass on fused references. Anything below this
    /// score is dropped from the LLM's context (to save tokens + sharpen focus).
    /// Lower than the image rerank threshold (0.40) because text rerank tends to
    /// score more leniently — most candidates have at least token-level overlap.
    /// </summary>
    private const double TextRerankThreshold = 0.20;

    /// <summary>
    /// Hard cap on how many references the recommendation LLM sees in its user-text
    /// after rerank. Past this point we get diminishing returns and waste tokens.
    /// </summary>
    private const int MaxReferencesAfterRerank = 10;

    /// <summary>
    /// Threshold for the per-post video-segment rerank. Segments scoring below this
    /// against the topic are not surfaced to the LLM — better to omit a weak segment
    /// than to mislead the LLM with off-topic transcript context. Set slightly higher
    /// than <see cref="TextRerankThreshold"/> because video transcripts are richer
    /// content (longer, more specific) and a weak score is more meaningful.
    /// </summary>
    private const double VideoSegmentRerankThreshold = 0.25;

    /// <summary>
    /// Per-post cap on how many segments are surfaced. Picking ONE was the original
    /// design (single most-relevant moment); allowing up to N means we can show the
    /// LLM both "what the post is about" + "the strongest supporting moment" when a
    /// single video has multiple relevant scenes (e.g. product intro + feature demo).
    /// </summary>
    private const int MaxVideoSegmentsPerPost = 2;

    private const string SystemPrompt =
        "You are a social-media content strategist whose JOB is to decide what the page should post next. " +
        "This API has two operating modes:\n" +
        "  (1) USER-SUPPLIED TOPIC — the user gave a specific topic; treat it as the brief and write " +
        "the recommendation for that topic.\n" +
        "  (2) AUTO-DISCOVERY (\"lazy-user\" mode) — the user did NOT give a topic. This is a " +
        "first-class flow, not a fallback. You must invent the next-post topic yourself by:\n" +
        "      (a) reading the page profile + past posts in the included context to identify the " +
        "page's content pillars (its niche, voice, recurring themes, audience),\n" +
        "      (b) calling web search to find what is CURRENTLY trending in those pillars right now " +
        "(industry news this week, recent product launches in the niche, seasonal events, popular " +
        "discussions). In auto-discovery mode web search is REQUIRED — auto-discovery cannot rely on " +
        "cached context alone, since the goal is timely content,\n" +
        "      (c) picking ONE specific, concrete topic that is BOTH on-brand for this page AND " +
        "timely. Do NOT pick a generic topic, do NOT pick something outside the page's niche, do " +
        "NOT duplicate a topic the page recently covered. State the chosen topic explicitly at the " +
        "top of your answer (in the page's language).\n\n" +
        "Inputs you receive: (a) the page profile — name, introduction (About / Giới thiệu), " +
        "category, website, email, phone, location, " +
        "(b) a text context block describing the user's past posts and engagement, " +
        "(c) a few reference images from past posts retrieved for the question/topic, " +
        "(d) content guidance — copywriting formulas (FAB/BAB/AIDA/etc.), viral-hook frameworks, " +
        "engagement-trigger tactics, visual-design rules, platform-specific algorithm signals. " +
        "Treat the page profile as the BRAND ANCHOR — every suggestion must align with what the " +
        "page is actually about. Do not drift off-brand even if a topic would be 'trending'.\n\n" +
        "LANGUAGE: detect the page's primary language from its introduction text first, then the " +
        "page's name, then recent past captions — in that order of priority. Your ENTIRE answer " +
        "(including any draft caption text you suggest) must be written in that language, regardless " +
        "of what language the user's question / instruction happens to be in.\n\n" +
        "CONTACT INFO: When recommending posts whose intent is CTA / promotion / brand awareness " +
        "/ product launch, naturally weave in the page's website, email, or phone (from the page " +
        "profile section above) into the draft caption. Skip contact info for purely educational / " +
        "storytelling / opinion posts. Any CTA phrasing must be in the page's language. " +
        "VERBATIM-ONLY: when you do include any contact field, copy the value EXACTLY from the " +
        "Page profile section — do NOT shorten, normalize, or rewrite. In particular: do NOT " +
        "swap an unusual TLD for '.com'; do NOT drop subdomains/paths; do NOT replace a specific " +
        "email with a canonical one ('contact@', 'info@', 'hello@', 'support@'); do NOT invent a " +
        "phone number that is not in the profile. If a field is absent from the profile, OMIT it.\n\n" +
        "WEB SEARCH: you have web-search ability. Use it whenever the work requires fresh / current " +
        "information — ALWAYS in auto-discovery mode (to find what is trending in the page's pillars), " +
        "and in user-supplied-topic mode only when the topic itself involves recent events / launches " +
        "/ news. Do NOT search for general best-practices, formulas, or the user's own past data — " +
        "those come from the included context. When you do search, cite specific URLs and paraphrase " +
        "concisely; do NOT dump search results.\n\n" +
        "Use the images as visual evidence — describe and reference them when relevant. " +
        "Apply the content guidance as a writing structure — pick whichever formula or hook fits " +
        "best for the chosen topic and target platform, and call out by name which one you used. " +
        "Be concrete, cite which past post(s) you're drawing from, and propose actionable next steps.";

    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IRagClient _ragClient;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly IRerankClient _rerankClient;
    private readonly ILogger<QueryAccountRecommendationsQueryHandler> _logger;

    public QueryAccountRecommendationsQueryHandler(
        IUserSocialMediaService userSocialMediaService,
        IRagClient ragClient,
        IMultimodalLlmClient multimodalLlm,
        IRerankClient rerankClient,
        ILogger<QueryAccountRecommendationsQueryHandler> logger)
    {
        _userSocialMediaService = userSocialMediaService;
        _ragClient = ragClient;
        _multimodalLlm = multimodalLlm;
        _rerankClient = rerankClient;
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

        // Fan-out: account RAG + platform-pinned formula list + semantic knowledge.
        // Account RAG returns rich multimodal hits; the two knowledge calls return
        // pure text context (formulas + tactical heuristics) we'll inject as guidance.
        var ragTask = _ragClient.MultimodalQueryAsync(
            new RagMultimodalQueryRequest(
                Query: request.Query,
                DocumentIdPrefix: prefix,
                TopK: topK,
                Modes: new[] { "text", "visual", "video" },
                Platform: platform,
                SocialMediaId: request.SocialMediaId.ToString("N")),
            cancellationToken);

        var platformPinnedTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: $"primary content formulas for {platform}",
                DocumentIdPrefix: $"knowledge:content-formulas:platform-mapping-{platform}",
                Mode: "naive",
                TopK: 2,
                OnlyNeedContext: true),
            cancellationToken);

        var semanticKnowledgeTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: request.Query,
                DocumentIdPrefix: "knowledge:",
                Mode: "naive",
                TopK: 3,
                OnlyNeedContext: true),
            cancellationToken);

        // Page profile (About / category / website / location / bio) — ingested
        // by the indexer as docId `<prefix>profile`. Always-on so the LLM grounds
        // every answer in what the page is actually about, not just past posts.
        var profileTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: "page profile and introduction",
                DocumentIdPrefix: $"{prefix}profile",
                Mode: "naive",
                TopK: 1,
                OnlyNeedContext: true),
            cancellationToken);

        // Tasks run in parallel; awaiting each one in turn is fine since they were started concurrently.
        var rag = await ragTask;
        var platformPinned = await SafeQuery(platformPinnedTask, "platform-pinned knowledge");
        var semanticKnowledge = await SafeQuery(semanticKnowledgeTask, "semantic knowledge");
        var pageProfile = await SafeQuery(profileTask, "page profile");

        // ── RAG VISIBILITY ────────────────────────────────────────────────
        // Dump what each RAG leg actually retrieved so the operator can see
        // what context the LLM is about to receive. Truncate per-field to keep
        // logs readable; full content is in the user-prompt block we build below.
        _logger.LogInformation(
            "RAG retrieval for socialMediaId={SocialMediaId} platform={Platform}: " +
            "accountText={TextLen} chars, visualHits={VisualCount}, videoHits={VideoCount}, " +
            "platformPinned={PlatformLen} chars, semanticKnowledge={KnowledgeLen} chars, " +
            "pageProfile={ProfileLen} chars",
            request.SocialMediaId, platform,
            (rag.Text?.Context ?? string.Empty).Length,
            rag.Visual?.Count ?? 0,
            rag.Video?.Count ?? 0,
            (platformPinned?.Answer ?? string.Empty).Length,
            (semanticKnowledge?.Answer ?? string.Empty).Length,
            (pageProfile?.Answer ?? string.Empty).Length);

        if (!string.IsNullOrWhiteSpace(pageProfile?.Answer))
        {
            _logger.LogInformation(
                "RAG[pageProfile] for socialMediaId={SocialMediaId}:\n{Profile}",
                request.SocialMediaId, Truncate(pageProfile!.Answer, 1500));
        }
        if (!string.IsNullOrWhiteSpace(platformPinned?.Answer))
        {
            _logger.LogInformation(
                "RAG[platformPinned] for socialMediaId={SocialMediaId}:\n{PlatformPinned}",
                request.SocialMediaId, Truncate(platformPinned!.Answer, 1500));
        }
        if (!string.IsNullOrWhiteSpace(semanticKnowledge?.Answer))
        {
            _logger.LogInformation(
                "RAG[semanticKnowledge] for socialMediaId={SocialMediaId}:\n{Knowledge}",
                request.SocialMediaId, Truncate(semanticKnowledge!.Answer, 1500));
        }
        if (!string.IsNullOrWhiteSpace(rag.Text?.Context))
        {
            _logger.LogInformation(
                "RAG[accountText] for socialMediaId={SocialMediaId}:\n{Text}",
                request.SocialMediaId, Truncate(rag.Text!.Context, 1500));
        }

        if (rag.VisualError != null)
        {
            _logger.LogWarning(
                "Visual retrieval failed for socialMediaId={SocialMediaId}: {Error}",
                request.SocialMediaId,
                rag.VisualError);
        }
        if (rag.VideoError != null)
        {
            _logger.LogWarning(
                "Video retrieval failed for socialMediaId={SocialMediaId}: {Error}",
                request.SocialMediaId,
                rag.VideoError);
        }

        // Reciprocal Rank Fusion: collapse text matched-doc-ids and visual hits
        // into a single ranked list of distinct postIds. Visual hits carry the
        // image_url payload we'll feed back to the multimodal LLM.
        var fusedReferencesRaw = FuseReferences(rag, prefix);

        // Rerank pass 1 — within each video post that has multiple matched
        // segments, pick the segment whose transcript is most relevant. Replaces
        // the "first segment wins" RRF behavior with "best-by-relevance wins".
        var fusedAfterSegmentPick = await PickBestVideoSegmentsAsync(
            request.Query, fusedReferencesRaw, rag.Video, cancellationToken);

        // Rerank pass 2 — text rerank across ALL fused references using their
        // captions + (best) transcripts. Drops low-relevance items below threshold,
        // caps at MaxReferencesAfterRerank to keep the recommendation LLM's user-text
        // focused. Falls back to the un-reranked order if Jina returns empty.
        var fusedReferences = await RerankReferencesByTextAsync(
            request.Query, fusedAfterSegmentPick, cancellationToken);

        // Prefer the S3-mirror URL (fetchable by OpenAI / OpenRouter) over the raw
        // FB CDN URL (which they refuse). Old Qdrant points without mirror_s3_key
        // fall back to ImageUrl as a best-effort — they'll likely fail the LLM
        // image fetch but won't break the call (other refs may still resolve).
        var imageRefsForLlm = fusedReferences
            .Select(r => r.MirroredImageUrl ?? r.ImageUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxImagesToLlm)
            .ToList();

        var userTextBuilder = new StringBuilder();
        userTextBuilder.AppendLine($"User question: {request.Query}");
        userTextBuilder.AppendLine($"Target platform: {platform}");
        userTextBuilder.AppendLine();
        if (!string.IsNullOrWhiteSpace(pageProfile?.Answer))
        {
            userTextBuilder.AppendLine("=== Page profile (the account's About / category / website / location) ===");
            userTextBuilder.AppendLine("Treat this as the page's identity. Every recommendation MUST be consistent with this brand positioning.");
            userTextBuilder.AppendLine(pageProfile!.Answer);
            userTextBuilder.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(platformPinned?.Answer))
        {
            userTextBuilder.AppendLine($"=== {platform.ToUpperInvariant()} platform formula mapping (always apply) ===");
            userTextBuilder.AppendLine(platformPinned!.Answer);
            userTextBuilder.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(semanticKnowledge?.Answer))
        {
            userTextBuilder.AppendLine("=== Content guidance matched to the question (formulas / hooks / engagement / design / algorithm) ===");
            userTextBuilder.AppendLine(semanticKnowledge!.Answer);
            userTextBuilder.AppendLine();
        }
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
                if (!string.IsNullOrWhiteSpace(r.VideoSegmentTime))
                {
                    userTextBuilder.AppendLine(
                        $"     videoSegment time={r.VideoSegmentTime} transcript=\"{Truncate(r.VideoTranscript, 240)}\"");
                }
            }
            userTextBuilder.AppendLine();
        }
        if (imageRefsForLlm.Count > 0)
        {
            userTextBuilder.AppendLine($"The next {imageRefsForLlm.Count} attached image(s) are the actual reference images from those posts, in the same order. Use them as visual evidence when answering.");
        }

        var userText = userTextBuilder.ToString();
        _logger.LogInformation(
            "LLM[recommendation] INPUT for socialMediaId={SocialMediaId} ({UserTextLen} chars, {ImageCount} ref images):\n{UserText}",
            request.SocialMediaId, userText.Length, imageRefsForLlm.Count, Truncate(userText, 4000));

        MultimodalAnswerResult llmResult;
        try
        {
            llmResult = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: SystemPrompt,
                    UserText: userText,
                    ReferenceImageUrls: imageRefsForLlm),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multimodal LLM call failed for socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<AccountRecommendationsAnswer>(
                new Error("Recommendations.LlmFailed", $"Answer generation failed: {ex.Message}"));
        }

        _logger.LogInformation(
            "LLM[recommendation] OUTPUT for socialMediaId={SocialMediaId} ({AnswerLen} chars, {SourceCount} web sources):\n{Answer}",
            request.SocialMediaId, llmResult.Answer.Length, llmResult.Sources.Count, Truncate(llmResult.Answer, 4000));

        if (llmResult.Sources.Count > 0)
        {
            for (var i = 0; i < Math.Min(llmResult.Sources.Count, 5); i++)
            {
                var s = llmResult.Sources[i];
                _logger.LogInformation(
                    "  source[{Idx}] title={Title} url={Url}",
                    i, s.Title, s.Url);
            }
        }

        return Result.Success(new AccountRecommendationsAnswer(
            Answer: llmResult.Answer,
            DocumentIdPrefix: prefix,
            References: fusedReferences,
            WebSources: llmResult.Sources.Count > 0 ? llmResult.Sources : null,
            PageProfileText: pageProfile?.Answer));
    }

    /// <summary>
    /// Per-video-post segment selection: when one post has multiple matched segments
    /// in <paramref name="videoHits"/>, rerank those transcripts against the query
    /// and overwrite the reference's <c>VideoSegmentTime</c> + <c>VideoTranscript</c>
    /// with the best-scoring segment. The original behavior in <see cref="FuseReferences"/>
    /// just kept the first matched segment per post (RRF order); this picks by relevance.
    ///
    /// Falls back to the original references if rerank fails / returns empty / there
    /// are no posts with multiple matched segments.
    /// </summary>
    private async Task<List<RecommendationReference>> PickBestVideoSegmentsAsync(
        string query,
        List<RecommendationReference> refs,
        IReadOnlyList<RagVideoSegmentHit>? videoHits,
        CancellationToken cancellationToken)
    {
        if (videoHits is null || videoHits.Count <= 1 || refs.Count == 0)
        {
            return refs;
        }

        // Group hits by post-key (same key the fusion logic uses). Only posts with
        // 2+ matched segments need rerank — single-segment posts already have one
        // canonical choice.
        var multiSegmentPosts = videoHits
            .Where(h => !string.IsNullOrWhiteSpace(h.Transcript))
            .GroupBy(h => h.PostId ?? h.VideoName ?? string.Empty)
            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
            .ToList();

        if (multiSegmentPosts.Count == 0)
        {
            return refs;
        }

        // Build a lookup of refs by their fusion key so we can update the matching ref.
        var refByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < refs.Count; i++)
        {
            var key = refs[i].PostId ?? refs[i].DocumentId ?? string.Empty;
            if (!string.IsNullOrEmpty(key) && !refByKey.ContainsKey(key))
            {
                refByKey[key] = i;
            }
        }

        var updated = refs.ToList();
        foreach (var group in multiSegmentPosts)
        {
            if (!refByKey.TryGetValue(group.Key, out var refIdx))
            {
                continue;
            }
            var segments = group.ToList();
            var docs = segments
                .Select(s => new RerankDocument(Text: s.Transcript ?? string.Empty))
                .ToList();

            IReadOnlyList<RerankResult> scored;
            try
            {
                scored = await _rerankClient.RerankAsync(query, docs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Video segment rerank failed for postId={PostId}; keeping original segment",
                    group.Key);
                continue;
            }
            if (scored.Count == 0) continue;

            // Apply threshold + cap (mirrors the text + image rerank passes). Segments
            // below the threshold are dropped; we keep at most MaxVideoSegmentsPerPost
            // of the remaining, in score order. If everything is below threshold the
            // segment fields are CLEARED on the ref — better to surface no transcript
            // context than a misleading one.
            var keptSegments = scored
                .Where(r =>
                    r.Score >= VideoSegmentRerankThreshold &&
                    r.Index >= 0 && r.Index < segments.Count)
                .OrderByDescending(r => r.Score)
                .Take(MaxVideoSegmentsPerPost)
                .Select(r => (Hit: segments[r.Index], r.Score))
                .ToList();

            if (keptSegments.Count == 0)
            {
                updated[refIdx] = updated[refIdx] with
                {
                    VideoSegmentTime = null,
                    VideoTranscript = null,
                    // Drop frame URLs too — if no segment passed threshold, none of
                    // their frames are topic-relevant either.
                    VideoFrameUrls = null,
                };
                _logger.LogInformation(
                    "Video segment rerank for postId={PostId}: ALL {Count} segments below threshold={Threshold:F2}; cleared segment fields",
                    group.Key, segments.Count, VideoSegmentRerankThreshold);
                continue;
            }

            // When multiple segments pass, concatenate them so the LLM sees both. The
            // single-segment record fields are kept as a flat string with explicit
            // timestamps prefixed so the model can quote them.
            var concatTimes = string.Join(", ", keptSegments.Select(s => s.Hit.Time ?? "?"));
            var concatTranscripts = string.Join(
                " | ",
                keptSegments.Select(s => $"[{s.Hit.Time ?? "?"}] {s.Hit.Transcript}"));

            // Narrow VideoFrameUrls to ONLY the frames belonging to the surviving
            // segments. We get there via each kept hit's FrameUrl (one URL per
            // segment, the highest-scoring sampled frame within that segment).
            var keptFrameUrls = keptSegments
                .Select(s => s.Hit.FrameUrl)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            updated[refIdx] = updated[refIdx] with
            {
                VideoSegmentTime = concatTimes,
                VideoTranscript = concatTranscripts,
                VideoFrameUrls = keptFrameUrls.Count > 0 ? keptFrameUrls : null,
            };

            _logger.LogInformation(
                "Video segment rerank for postId={PostId}: kept {Kept}/{Total} segments (threshold={Threshold:F2}, cap={Cap}); top score={TopScore:F3} time={TopTime}",
                group.Key, keptSegments.Count, segments.Count,
                VideoSegmentRerankThreshold, MaxVideoSegmentsPerPost,
                keptSegments[0].Score, keptSegments[0].Hit.Time);
        }

        return updated;
    }

    /// <summary>
    /// Text rerank across all fused references. Each ref's text candidate is its
    /// caption joined with the (best) video transcript when present. Reranks against
    /// the query, drops anything below <see cref="TextRerankThreshold"/>, sorts by
    /// score, caps at <see cref="MaxReferencesAfterRerank"/>. Used to (a) shrink
    /// the recommendation LLM's user-text and (b) put the most relevant past posts
    /// at the top of the citation list.
    ///
    /// Failure-tolerant: if rerank returns empty, we return the original list (RRF
    /// order) — no draft-blocking errors.
    /// </summary>
    private async Task<List<RecommendationReference>> RerankReferencesByTextAsync(
        string query,
        List<RecommendationReference> refs,
        CancellationToken cancellationToken)
    {
        if (refs.Count <= 1)
        {
            return refs;
        }

        var docs = refs.Select(r =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Caption)) parts.Add(r.Caption!);
            if (!string.IsNullOrWhiteSpace(r.VideoTranscript))
            {
                parts.Add("transcript: " + r.VideoTranscript);
            }
            var text = parts.Count > 0
                ? string.Join(" | ", parts)
                : (r.PostId ?? r.DocumentId ?? "post");
            return new RerankDocument(Text: text);
        }).ToList();

        IReadOnlyList<RerankResult> scored;
        try
        {
            scored = await _rerankClient.RerankAsync(query, docs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text rerank threw — keeping RRF order");
            return refs;
        }
        if (scored.Count == 0)
        {
            _logger.LogInformation("Text rerank returned 0 results — keeping RRF order");
            return refs;
        }

        // Log all scores for tuning visibility.
        var ordered = scored.OrderByDescending(r => r.Score).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            if (r.Index < 0 || r.Index >= refs.Count) continue;
            var c = refs[r.Index];
            var snippet = (c.Caption ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
            if (snippet.Length > 100) snippet = snippet[..100] + "…";
            _logger.LogInformation(
                "Text rerank rank {Rank}/{Total} score={Score:F3} src={Source} postId={PostId} caption=\"{Caption}\"",
                i + 1, ordered.Count, r.Score, c.Source, c.PostId ?? "n/a", snippet);
        }

        var kept = ordered
            .Where(r => r.Score >= TextRerankThreshold && r.Index >= 0 && r.Index < refs.Count)
            .Take(MaxReferencesAfterRerank)
            .Select(r => refs[r.Index])
            .ToList();

        _logger.LogInformation(
            "Text rerank kept {Kept}/{Total} refs (threshold={Threshold:F2}, cap={Cap})",
            kept.Count, refs.Count, TextRerankThreshold, MaxReferencesAfterRerank);

        return kept;
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
                    Score: 0d,
                    VideoSegmentTime: null,
                    VideoTranscript: null);
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
                    MirroredImageUrl = existing.MirroredImageUrl ?? hit.MirroredImageUrl,
                    Caption = existing.Caption ?? hit.Caption,
                    Source = MergeSource(existing.Source, "visual"),
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
                    Score: 0d,
                    VideoSegmentTime: null,
                    VideoTranscript: null,
                    MirroredImageUrl: hit.MirroredImageUrl);
            }
        }

        // Video leg — segments collapse to their parent post.
        var videoHits = rag.Video ?? Array.Empty<RagVideoSegmentHit>();
        for (var i = 0; i < videoHits.Count; i++)
        {
            var hit = videoHits[i];
            var key = hit.PostId ?? hit.VideoName ?? $"video:{i}";
            rrfScores[key] = rrfScores.GetValueOrDefault(key) + 1.0 / (RrfK + i + 1);

            // Collect per-segment frame URLs into the reference's list. When a single
            // post has multiple matched segments, each contributes its own best-frame URL.
            // Used downstream by the image-rerank pool so image-gen can pick the actual
            // relevant moment as a visual reference (instead of just the static thumbnail).
            string? frameUrl = string.IsNullOrWhiteSpace(hit.FrameUrl) ? null : hit.FrameUrl;

            if (byPostId.TryGetValue(key, out var existing))
            {
                var mergedFrames = MergeFrameUrls(existing.VideoFrameUrls, frameUrl);
                byPostId[key] = existing with
                {
                    Caption = existing.Caption ?? hit.Caption,
                    Source = MergeSource(existing.Source, "video"),
                    VideoSegmentTime = existing.VideoSegmentTime ?? hit.Time,
                    VideoTranscript = existing.VideoTranscript ?? hit.Transcript,
                    VideoFrameUrls = mergedFrames,
                };
            }
            else
            {
                byPostId[key] = new RecommendationReference(
                    DocumentId: hit.VideoName ?? key,
                    PostId: hit.PostId,
                    ImageUrl: null,
                    Caption: hit.Caption,
                    Source: "video",
                    Score: 0d,
                    VideoSegmentTime: hit.Time,
                    VideoTranscript: hit.Transcript,
                    VideoFrameUrls: frameUrl is null ? null : new[] { frameUrl });
            }
        }

        return byPostId
            .Select(kvp => kvp.Value with { Score = rrfScores[kvp.Key] })
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    /// <summary>
    /// Merge a new frame URL into the existing list (de-duped, max 6 retained per
    /// post — once we have that many distinct frames, additional segment matches
    /// rarely add visual variety worth scoring).
    /// </summary>
    private static IReadOnlyList<string>? MergeFrameUrls(IReadOnlyList<string>? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }
        var set = new List<string>(existing ?? Array.Empty<string>());
        if (!set.Contains(incoming, StringComparer.Ordinal))
        {
            set.Add(incoming);
        }
        if (set.Count > 6)
        {
            set = set.Take(6).ToList();
        }
        return set;
    }

    private static string MergeSource(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        var parts = a.Split('+', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        parts.Add(b);
        return string.Join('+', parts.OrderBy(s => s, StringComparer.Ordinal));
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

    private async Task<RagQueryResponse?> SafeQuery(Task<RagQueryResponse> task, string label)
    {
        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Label} fetch failed; continuing without it", label);
            return null;
        }
    }
}
