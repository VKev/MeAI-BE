using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
using Application.Abstractions.Search;
using Application.Recommendations.Commands;
using Application.Recommendations.Queries;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.Resources;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Recommendations;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

/// <summary>
/// End-to-end async draft-post generation:
///   1. Auto-index latest posts (skip-if-unchanged via fingerprint registry)
///   2. RAG multimodal query (text context + image references)
///   3. Caption generation (gpt-4o-mini multimodal, references attached as image_url parts)
///   4. Image generation (gpt-5.4-image-2 multimodal, same references for visual style)
///   5. Upload generated image to S3 via User microservice (data URL)
///   6. Create PostBuilder + Post via existing CreatePostCommand
///   7. Update DraftPostTask + publish notification
/// </summary>
public sealed class DraftPostGenerationConsumer : IConsumer<GenerateDraftPostStarted>
{
    /// <summary>
    /// Common preamble shared by all 3 style-specific caption prompts. Defines what
    /// inputs the LLM will see, language detection, and the no-web-search rule.
    /// </summary>
    private const string CaptionSystemPromptBase =
        "You are a social-media caption writer. You see (a) the user's topic for the next post, " +
        "(b) a RAG recommendation summary that already contains the page's profile (name, " +
        "introduction text, category, website, email, phone, location) AND may reference content " +
        "formulas (FAB/BAB/AIDA/etc.), viral-hook frameworks, engagement tactics, and design " +
        "heuristics — apply whichever formula or hook the summary recommends, " +
        "(c) recent post captions from the same account so you can match voice and style, and " +
        "(d) a few reference images from past posts.\n\n" +
        "LANGUAGE: Write the caption in the page's primary language. Detect this from the " +
        "language of the page's introduction / About text first, then the page's name, then " +
        "the language of recent past captions — in that order of priority. The caption MUST " +
        "match that language exactly, regardless of what language the user's topic prompt " +
        "happens to be in. Match emoji density and hashtag style of past captions.\n\n" +
        "PAGE NAME: Always reference the page name at least once (in the body, the sign-off, " +
        "or as a hashtag) so the caption clearly belongs to this brand.\n\n" +
        "CONTACT INFO — VERBATIM ONLY (CRITICAL): If you include the page's website, email, or " +
        "phone in the caption, you MUST copy each value EXACTLY as it appears in the " +
        "'=== Page profile ===' block of the user message. The profile block is the single " +
        "source of truth. Common hallucinations to avoid:\n" +
        "  - Do NOT shorten or normalize a URL by dropping or replacing its TLD (a profile URL " +
        "ending in '.website', '.app', '.io', '.dev', a country TLD, etc. must NOT be rewritten " +
        "to '.com').\n" +
        "  - Do NOT strip subdomains, paths, or query strings the profile URL has.\n" +
        "  - Do NOT replace a specific email (e.g. a personal-looking address, a name+suffix " +
        "address, a numeric address) with a canonical-sounding alternative like 'contact@', " +
        "'info@', 'hello@', or 'support@' on the same domain.\n" +
        "  - Do NOT invent a phone number when none is in the profile (no 'XXX-XXX-XXXX' " +
        "placeholders, no plausible-looking local-format guesses).\n" +
        "  - Do NOT invent any contact field that is missing from the profile — OMIT it.\n" +
        "If you are unsure whether a value is meant to be exactly as the profile shows it: " +
        "the answer is YES, copy verbatim.\n\n" +
        "FORMATTING — CRITICAL: The caption is rendered VERBATIM by Facebook / Instagram / " +
        "TikTok / Threads, none of which parse Markdown. Every Markdown character will appear " +
        "literally as punctuation (e.g. `**bold**` shows as the four asterisks plus the word). " +
        "Therefore your output MUST be plain text with NONE of the following:\n" +
        "  - NO `**` or `__` for bold\n" +
        "  - NO `*` or `_` for italic\n" +
        "  - NO `#`, `##`, `###` heading lines (a leading `#` followed by a space is a header, " +
        "different from a hashtag — a hashtag is `#OneWord` with no space)\n" +
        "  - NO `-` / `*` / `>` markdown bullets at the start of lines\n" +
        "  - NO Markdown links `[text](url)` — write the bare URL\n" +
        "  - NO inline code backticks or fenced code blocks\n" +
        "  - NO blockquote `>` lines\n" +
        "For emphasis use natural language, ALL CAPS sparingly, or emojis as visual anchors. " +
        "For lists use emoji bullets (e.g. 📸 / 🔍 / 👉 / ✨) followed by a single space then text, " +
        "OR plain numbered lines like '1. ', '2. ', '3. '. Separate sections with blank lines. " +
        "Hashtags must be in the form `#WordOrPhraseNoSpace` — they go on their own line(s) at " +
        "the end (or interleaved if past captions do that) and Facebook auto-renders them as " +
        "links. Bare URLs auto-render too — never wrap them in Markdown link syntax.\n\n" +
        "Do NOT use web search — the context is already provided, and a caption should not " +
        "contain inline URL citations. " +
        "Output the caption only — no preface, no numbering of the response itself, no Markdown.";

    /// <summary>
    /// Caption system prompt for <c>creative</c> style — pure mood/lifestyle. Caption is
    /// the only text channel (image carries no text), so it can be longer and more atmospheric.
    /// Contact info is OMITTED to keep the editorial feel; only the page name is mentioned.
    /// </summary>
    private const string CaptionSystemPromptCreative = CaptionSystemPromptBase + "\n\n" +
        "CONTACT INFO (creative style): The image carries NO text, so the caption is the only " +
        "verbal channel. Keep it editorial / atmospheric / story-driven. Do NOT include the " +
        "website URL, email, or phone unless the post is explicitly an event invite or RSVP. " +
        "The page name is enough brand presence.";

    /// <summary>
    /// Caption system prompt for <c>branded</c> (DEFAULT) style. Image carries the brand mark
    /// + an optional short headline; the caption explains and invites interaction. Contact
    /// info appears when the post is product- or service-related.
    /// </summary>
    private const string CaptionSystemPromptBranded = CaptionSystemPromptBase + "\n\n" +
        "CONTACT INFO (branded style — DEFAULT): When the post is about a product, service, " +
        "offering, or brand awareness, naturally weave in the page's website AND at least one " +
        "of email or phone from the profile. For purely educational / storytelling / engagement " +
        "posts, surface the website only (no phone/email) so it does not feel salesy. Use the " +
        "page's language for surrounding phrasing.";

    /// <summary>
    /// Caption system prompt for <c>marketing</c> style — full sales push. The image renders
    /// brand + headline + CTA + contact, AND the caption MUST repeat the contact info so the
    /// post stands on its own when shared, screenshotted, or read in a feed-preview.
    /// </summary>
    private const string CaptionSystemPromptMarketing = CaptionSystemPromptBase + "\n\n" +
        "CONTACT INFO (marketing style — MANDATORY): This is a promotional post. The caption " +
        "MUST include ALL of the following from the page profile: page name, website URL, " +
        "AND every contact channel that exists in the profile (email and/or phone). End the " +
        "caption with a clear call-to-action line in the page's language (e.g. 'Đặt hàng ngay', " +
        "'Order now', 'DM us to learn more') followed by a contact block. Open the caption with " +
        "a strong value-prop hook (the offer / benefit) in the first line. Use 3–6 hashtags " +
        "including the brand name as one of them.";

    /// <summary>
    /// Image-gen system prompt for <c>creative</c> — pure visual, NO text rendering.
    /// </summary>
    private const string ImageSystemPromptCreative =
        "You are an image-generation assistant for social media. Produce ONE editorial-style " +
        "image that fits the user's topic AND matches the visual style of the reference images " +
        "attached (color palette, lighting, composition, mood, subject framing). " +
        "DO NOT render any text, words, letters, logos, or watermarks on the image. " +
        "The image is purely photographic / illustrative — all copy lives in the caption, not the pixels. " +
        "Output an image, not text.";

    /// <summary>
    /// Image-gen system prompt for <c>branded</c> — hero visual with optional short headline + subtle brand mark.
    /// </summary>
    private const string ImageSystemPromptBranded =
        "You are an image-generation assistant for social media. Produce ONE branded image that " +
        "fits the user's topic AND matches the visual style of the reference images attached " +
        "(color palette, lighting, composition, mood). " +
        "If the prompt includes quoted text strings (headline or short subhead), render them EXACTLY " +
        "as quoted, with strong contrast and clean typography in a top-or-bottom safe area. " +
        "Place the brand mark / logo subtly in one corner (small, low-emphasis). " +
        "Do NOT add any text not present in the prompt — no invented taglines, no watermarks. " +
        "Output an image, not text.";

    /// <summary>
    /// Image-gen system prompt for <c>marketing</c> — full promo flyer with all quoted text rendered.
    /// </summary>
    private const string ImageSystemPromptMarketing =
        "You are an image-generation assistant producing a promotional / marketing image for social media. " +
        "Produce ONE high-contrast marketing image that fits the user's topic AND matches the brand " +
        "palette from the reference images attached. " +
        "RENDER EVERY QUOTED TEXT STRING in the prompt EXACTLY as quoted — headline, subhead, CTA " +
        "button text, and contact line (website / email / phone). Use clear typographic hierarchy: " +
        "headline largest and bold, subhead smaller, CTA inside a brand-colored button shape, " +
        "contact line smallest at the bottom. " +
        "Place the brand logo / wordmark prominently (top-left or top-right). " +
        "All text must be sharp, readable on mobile (image will be viewed at 400px wide), and use " +
        "WCAG-AAA contrast against its background. " +
        "Output an image, not text.";

    private static string CaptionSystemPromptFor(string style) => style switch
    {
        DraftPostStyles.Creative => CaptionSystemPromptCreative,
        DraftPostStyles.Marketing => CaptionSystemPromptMarketing,
        _ => CaptionSystemPromptBranded,
    };

    private static string ImageSystemPromptFor(string style) => style switch
    {
        DraftPostStyles.Creative => ImageSystemPromptCreative,
        DraftPostStyles.Marketing => ImageSystemPromptMarketing,
        _ => ImageSystemPromptBranded,
    };

    /// <summary>
    /// "Art-director" prompt — gpt-4o-mini reads everything the image-gen model can't
    /// (caption, RAG references, visual-design knowledge, video segment captions, transcripts,
    /// reference image pixels) and produces a focused, image-gen-friendly brief in JSON.
    ///
    /// The base prompt is shared; per-style addenda below dictate whether/how text is
    /// rendered on the image (none for creative, optional headline for branded, full
    /// promo stack for marketing).
    /// </summary>
    private const string ImageBriefSystemPromptBase =
        "You are an art director composing a brief for an image-generation model that has VERY limited " +
        "context — it cannot read RAG, see videos, or follow long instructions. " +
        "Your job: synthesize everything you know into a SHORT, vivid, concrete image-gen prompt. " +
        "You will see: (a) the page's own About / category / brand identity (treat as the brand " +
        "anchor — image must look like something this specific page would post), " +
        "(b) the post caption that just got written, (c) recent post images from this " +
        "account (use them to lock in palette / lighting / composition), (d) descriptions of past " +
        "video segments + transcripts when available, (e) STYLE-SPECIFIC design rules from the " +
        "RAG knowledge base (image-design-{style}) — these are AUTHORITATIVE and must be followed, " +
        "(f) the target platform, (g) the requested STYLE.\n\n" +
        "Output STRICT JSON only — no preface, no markdown, no code fences — with these keys:\n" +
        "  \"prompt\": string. The actual image-gen prompt. Be vivid and specific. Cap ~150 words. " +
        "Describe the SUBJECT first, then composition (rule of thirds, framing, safe areas for any " +
        "text overlay), then palette + lighting + mood, then visual style notes. Avoid repeating " +
        "the caption verbatim. Follow the per-style rules below for any on-image text.\n" +
        "  \"style_notes\": string. Short list of style constraints repeated as a system prompt to " +
        "reinforce brand consistency (e.g. \"flat illustration, vibrant gradient palette, " +
        "high-contrast text overlay if any, mobile-first composition\"). Cap ~80 words.\n" +
        "  \"aspect_ratio\": one of \"1:1\" (feed posts), \"9:16\" (reels / stories / TikTok), " +
        "\"4:5\" (IG portrait), \"16:9\" (YouTube / FB cover). Pick based on the platform + post type.";

    private const string ImageBriefStyleAddendumCreative = "\n\n" +
        "STYLE = creative (mood / editorial). Your prompt must produce an image with NO rendered " +
        "text whatsoever — no headlines, no logos, no watermarks, no captions on the pixels. " +
        "Single hero subject, atmospheric, photographic. Lead with subject + composition + light. " +
        "Do NOT include any quoted text strings the image-gen model might try to render. " +
        "Example final prompt fragment: \"A close-up of a banh mi sandwich on a rustic wooden " +
        "board, golden-hour lighting from the left, soft bokeh background…\" (no quoted overlay text).";

    private const string ImageBriefStyleAddendumBranded = "\n\n" +
        "STYLE = branded (DEFAULT — hero visual + subtle brand mark + optional short headline). " +
        "Your prompt should describe a strong photographic / illustrative scene PLUS one short " +
        "on-image headline of 3–8 words rendered in the page's primary language, in a " +
        "headline-safe area (top third or bottom third). Quote the headline EXACTLY in the prompt " +
        "so the image-gen model renders it verbatim, e.g. ...with the bold headline " +
        "\\\"Your camera, smarter.\\\" rendered in white sans-serif at the bottom-left... " +
        "Add a small subtle brand wordmark in a corner. " +
        "Do NOT add CTAs, contact info, or multiple text layers — those are marketing-style only.";

    private const string ImageBriefStyleAddendumMarketing = "\n\n" +
        "STYLE = marketing (full promo flyer). Your prompt MUST instruct the image-gen model to " +
        "render ALL of the following on the image, each quoted EXACTLY in the page's primary " +
        "language so the model treats them as literal text:\n" +
        "  - Headline (3–6 words, the value prop / offer) — largest text, top or upper third\n" +
        "  - Subhead (4–10 words, the proof / detail) — smaller, under headline\n" +
        "  - CTA button text (1–3 words, e.g. \\\"Shop Now\\\", \\\"Đặt ngay\\\") — inside a " +
        "brand-colored rounded-rectangle button shape\n" +
        "  - Contact line (page website + at least one of email/phone, separated by middle-dots) " +
        "— smallest text, very bottom\n" +
        "  - Brand logo / wordmark — top-left or top-right, prominent\n" +
        "Use high contrast. The image must read as a PROMOTIONAL POSTER, not a lifestyle photo. " +
        "Pull the actual headline value-prop and contact info from the page profile + caption " +
        "context — do not invent a website or phone number that is not in the source data. " +
        "If a piece of contact info is missing from the profile, simply omit it.";

    private static string ImageBriefSystemPromptFor(string style) => style switch
    {
        DraftPostStyles.Creative => ImageBriefSystemPromptBase + ImageBriefStyleAddendumCreative,
        DraftPostStyles.Marketing => ImageBriefSystemPromptBase + ImageBriefStyleAddendumMarketing,
        _ => ImageBriefSystemPromptBase + ImageBriefStyleAddendumBranded,
    };

    /// <summary>
    /// Recommendation-query text used when the user did NOT supply a topic. This is
    /// the first-class "lazy user" flow — we explicitly tell the recommendation LLM
    /// it must auto-discover a topic by analyzing the page's content pillars (already
    /// in its RAG context) AND web-searching for what is currently trending.
    ///
    /// Today's date is injected at call time so the LLM cannot default to its
    /// training-cutoff year (we observed gpt-4o-mini picking "2023" content otherwise).
    ///
    /// The exact same recommendation handler runs; only this query text changes. The
    /// system prompt for that handler already covers auto-discovery as a first-class
    /// mode, so the LLM follows the right playbook.
    /// </summary>
    private static string BuildAutoTopicRecommendationQuery(DateTime nowUtc)
    {
        var today = nowUtc.ToString("yyyy-MM-dd");
        var year = nowUtc.Year.ToString();
        return
            $"AUTO-DISCOVERY MODE. Today's date is {today} (year {year}). " +
            "I did not give you a specific topic. Pick the next best post for this page yourself. " +
            "Analyze the page profile + past posts in the context to identify the brand's content pillars, " +
            $"USE WEB SEARCH to find what is currently trending in those pillars in {year} (this is required — " +
            "do NOT recall trends from your training data, since the latest cutoff likely predates today). " +
            "When picking a topic, anchor it explicitly in the current year " + year + " or the most recent " +
            "month / quarter where applicable; do NOT title or frame the post around an older year (no '2022', " +
            "'2023', etc. in the headline) unless you are deliberately retrospecting. " +
            "Pick ONE concrete topic that is on-brand AND timely. " +
            "State the chosen topic explicitly at the top of your answer in the page's primary language. " +
            "Then write the full post recommendation for that topic — caption, formula used, visual " +
            "suggestions, engagement strategy.";
    }

    /// <summary>
    /// LLM-produced brief for the image-gen model. Synthesizes RAG context (caption, refs,
    /// video segments, knowledge base) into a focused brief — image-gen models perform
    /// better with terse, vivid prompts than with walls of context.
    /// </summary>
    private sealed record ImageBrief(string Prompt, string StyleNotes, string AspectRatio);

    /// <summary>
    /// Cohere Rerank 4 Pro relevance scores are roughly probabilistic (0..1). Production
    /// guidance puts ~0.4–0.5 as the empirical "this is genuinely on-topic" floor.
    /// Below this we drop the candidate even if the per-draft cap isn't filled —
    /// better to send fewer good refs than to dilute with weak ones.
    /// </summary>
    private const double RerankRelevanceThreshold = 0.40;

    /// <summary>
    /// Default retrieval pool when reranking is in play. We deliberately retrieve
    /// MORE than the per-draft cap (msg.MaxReferenceImages, up to 8) so the reranker
    /// has a real choice — picking 4 of 14 is meaningfully better than picking 4 of 4.
    /// </summary>
    private const int DefaultRerankCandidatePool = 14;

    private readonly IMediator _mediator;
    private readonly IDraftPostTaskRepository _taskRepository;
    private readonly IPostRepository _postRepository;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly IImageGenerationClient _imageGenClient;
    private readonly IUserResourceService _userResourceService;
    private readonly IRagClient _ragClient;
    private readonly IImageSearchClient _imageSearchClient;
    private readonly IRerankClient _rerankClient;
    private readonly Application.Recommendations.Services.IQueryRewriter _queryRewriter;
    private readonly ILogger<DraftPostGenerationConsumer> _logger;

    public DraftPostGenerationConsumer(
        IMediator mediator,
        IDraftPostTaskRepository taskRepository,
        IPostRepository postRepository,
        IMultimodalLlmClient multimodalLlm,
        IImageGenerationClient imageGenClient,
        IUserResourceService userResourceService,
        IRagClient ragClient,
        IImageSearchClient imageSearchClient,
        IRerankClient rerankClient,
        Application.Recommendations.Services.IQueryRewriter queryRewriter,
        ILogger<DraftPostGenerationConsumer> logger)
    {
        _mediator = mediator;
        _taskRepository = taskRepository;
        _postRepository = postRepository;
        _multimodalLlm = multimodalLlm;
        _imageGenClient = imageGenClient;
        _userResourceService = userResourceService;
        _ragClient = ragClient;
        _imageSearchClient = imageSearchClient;
        _rerankClient = rerankClient;
        _queryRewriter = queryRewriter;
        _logger = logger;
    }

    private async Task PublishThinkingAsync(
        ConsumeContext<GenerateDraftPostStarted> context,
        DraftPostTask task,
        string action,
        string title,
        string message,
        object? details,
        CancellationToken cancellationToken,
        string phaseStatus = "processing")
    {
        var createdAt = DateTimeExtensions.PostgreSqlUtcNow;

        try
        {
            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    task.UserId,
                    NotificationTypes.AiDraftPostGenerationThinking,
                    title,
                    message,
                    new
                    {
                        correlationId = task.CorrelationId,
                        draftPostId = task.ResultPostId,
                        postId = task.ResultPostId,
                        socialMediaId = task.SocialMediaId,
                        workspaceId = task.WorkspaceId,
                        taskStatus = task.Status,
                        phaseStatus,
                        action,
                        details,
                        createdAt,
                    },
                    createdAt: createdAt,
                    source: NotificationSourceConstants.Creator),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "DraftPost {Id}: failed to publish thinking notification action={Action}",
                task.Id,
                action);
        }
    }

    public async Task Consume(ConsumeContext<GenerateDraftPostStarted> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;
        // Style is normalized at command-handler time, but we re-normalize here so
        // older queued messages (with no Style field) and any direct re-publishes
        // both default cleanly to "branded".
        var style = DraftPostStyles.NormalizeOrDefault(msg.Style);
        var isAutoTopic = msg.IsAutoTopic;

        _logger.LogInformation(
            "DraftPost: starting CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId} Style={Style} AutoTopic={Auto}",
            msg.CorrelationId, msg.UserId, msg.SocialMediaId, style, isAutoTopic);

        var task = await _taskRepository.GetByCorrelationIdForUpdateAsync(msg.CorrelationId, ct);
        if (task is null)
        {
            _logger.LogWarning("DraftPost: task not found for CorrelationId={CorrelationId}", msg.CorrelationId);
            return;
        }

        // Tracks whether the empty Post (pre-created by StartDraftPostGenerationCommand)
        // has been finalized with caption + image. If we fail BEFORE this flips true,
        // the catch path soft-deletes the empty Post so the FE doesn't see a permanent
        // blank placeholder. After it's true, the Post has real content and we keep it.
        bool postFinalized = false;

        try
        {
            task.Status = DraftPostTaskStatuses.Processing;
            task.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            await _taskRepository.SaveChangesAsync(ct);

            await PublishThinkingAsync(
                context,
                task,
                "generation_started",
                "AI recommendation started",
                "AI started preparing your recommendation draft.",
                new
                {
                    style,
                    userPrompt = msg.UserPrompt,
                    isAutoTopic,
                    topK = msg.TopK,
                    maxReferenceImages = msg.MaxReferenceImages,
                    maxRagPosts = msg.MaxRagPosts,
                },
                ct);

            // Step 0 — block until rag-microservice's lazy knowledge bootstrap is done.
            // After the first cold call this returns instantly. Doing this BEFORE Step 1
            // means every downstream RAG/LLM/image-gen call runs against a fully-built
            // knowledge index, never a half-built one.
            _logger.LogInformation("DraftPost {Id}: waiting for RAG to be ready...", task.Id);
            await PublishThinkingAsync(
                context,
                task,
                "rag_ready_wait_started",
                "AI is checking knowledge",
                "AI is waiting for the RAG knowledge service to be ready.",
                new
                {
                    waitTarget = "rag-microservice",
                    reason = "Knowledge search, caption writing, and image planning need the RAG index ready.",
                },
                ct);
            await _ragClient.WaitForRagReadyAsync(ct);
            _logger.LogInformation("DraftPost {Id}: RAG ready", task.Id);
            await PublishThinkingAsync(
                context,
                task,
                "rag_ready_wait_completed",
                "Knowledge service is ready",
                "AI can now read account and knowledge context.",
                new
                {
                    waitTarget = "rag-microservice",
                },
                ct,
                phaseStatus: "completed");

            // Step 1 — auto-index. Existing skip-if-unchanged logic ensures only new/changed
            // posts hit RAG; unchanged ones are no-ops.
            var indexMaxPosts = msg.MaxRagPosts > 0 ? msg.MaxRagPosts : 30;
            _logger.LogDebug("DraftPost {Id}: indexing posts (max={Max})...", task.Id, indexMaxPosts);
            await PublishThinkingAsync(
                context,
                task,
                "account_posts_indexing_started",
                "AI is reading account posts",
                "AI is checking recent account posts before writing the recommendation.",
                new
                {
                    socialMediaId = msg.SocialMediaId,
                    maxPosts = indexMaxPosts,
                    purpose = "Find new or changed account content and make it available to RAG.",
                },
                ct);
            var indexResult = await _mediator.Send(
                new IndexSocialAccountPostsCommand(msg.UserId, msg.SocialMediaId, indexMaxPosts), ct);
            if (indexResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Indexing failed: {indexResult.Error.Code} {indexResult.Error.Description}");
            }
            _logger.LogInformation(
                "DraftPost {Id}: indexed total={Total} new={New} updated={Updated} unchanged={Unchanged}",
                task.Id,
                indexResult.Value.TotalPostsScanned,
                indexResult.Value.NewPosts,
                indexResult.Value.UpdatedPosts,
                indexResult.Value.UnchangedPosts);
            await PublishThinkingAsync(
                context,
                task,
                "account_posts_indexing_completed",
                "Account posts were checked",
                "AI finished syncing recent account content into knowledge.",
                new
                {
                    socialMediaId = indexResult.Value.SocialMediaId,
                    platform = indexResult.Value.Platform,
                    documentIdPrefix = indexResult.Value.DocumentIdPrefix,
                    totalPostsScanned = indexResult.Value.TotalPostsScanned,
                    newPosts = indexResult.Value.NewPosts,
                    updatedPosts = indexResult.Value.UpdatedPosts,
                    unchangedPosts = indexResult.Value.UnchangedPosts,
                    queuedTextDocuments = indexResult.Value.QueuedTextDocuments,
                    queuedImageDocuments = indexResult.Value.QueuedImageDocuments,
                    queuedVideoDocuments = indexResult.Value.QueuedVideoDocuments,
                    queuedProfileDocuments = indexResult.Value.QueuedProfileDocuments,
                },
                ct,
                phaseStatus: "completed");

            // Step 2 — RAG multimodal query. Reuses the same retrieval as /query: text context
            // + visual hits with image URLs. In auto-topic mode we substitute a
            // first-class auto-discovery instruction; the recommendation system prompt
            // already treats this as a primary mode (not an exception).
            var recommendationQuery = isAutoTopic
                ? BuildAutoTopicRecommendationQuery(DateTime.UtcNow)
                : msg.UserPrompt;
            await PublishThinkingAsync(
                context,
                task,
                "query_rewrite_started",
                "AI is planning the knowledge search",
                "AI is rewriting the request into search terms for RAG and visual retrieval.",
                new
                {
                    style,
                    isAutoTopic,
                    recommendationQuery,
                },
                ct);

            // Step 1.5 — query rewriter. ONE LLM call up-front; outputs feed every
            // retrieval/rerank query in QueryAccountRecommendationsQuery handler AND
            // the downstream style-knowledge fetch (Step 3.4) + image-pool rerank
            // (Step 3.35). Threaded into the query via PrecomputedRewrite so the
            // handler doesn't repeat the LLM call.
            var rewriteResult = await _queryRewriter.RewriteAsync(
                new Application.Recommendations.Services.QueryRewriteRequest(
                    UserPrompt: recommendationQuery,
                    PageProfileSnippet: null,         // handler will be called next; we
                                                       // could fetch profile here first but
                                                       // it adds an extra round-trip. Hand
                                                       // off without profile — handler does
                                                       // the rewrite+intent classification
                                                       // adequately on prompt+platform alone.
                    Platform: null,                   // handler resolves platform via gRPC
                    Style: style),
                ct);
            var rewrite = rewriteResult.IsSuccess
                ? rewriteResult.Value
                : new Application.Recommendations.Services.QueryRewriteResult(
                    Language: "en",
                    Intent: "informational",
                    PrimaryQuery: recommendationQuery,
                    AltQueries: Array.Empty<string>(),
                    VisualQuery: recommendationQuery,
                    KeyTerms: Array.Empty<string>());
            _logger.LogInformation(
                "DraftPost {Id}: rewriter lang={Lang} intent={Intent} primary={Primary} visual={Visual} keyTerms=[{KeyTerms}]",
                task.Id, rewrite.Language, rewrite.Intent,
                Truncate(rewrite.PrimaryQuery, 180),
                Truncate(rewrite.VisualQuery, 180),
                string.Join(", ", rewrite.KeyTerms));
            await PublishThinkingAsync(
                context,
                task,
                "query_rewrite_completed",
                "AI planned the knowledge search",
                "AI created the text, visual, and keyword queries used for retrieval.",
                new
                {
                    rewriteResult.IsSuccess,
                    language = rewrite.Language,
                    intent = rewrite.Intent,
                    primaryQuery = rewrite.PrimaryQuery,
                    altQueries = rewrite.AltQueries,
                    visualQuery = rewrite.VisualQuery,
                    keyTerms = rewrite.KeyTerms,
                    sourceQuery = recommendationQuery,
                },
                ct,
                phaseStatus: "completed");

            _logger.LogDebug(
                "DraftPost {Id}: querying RAG (autoTopic={Auto}, queryLen={Len})...",
                task.Id, isAutoTopic, recommendationQuery.Length);
            await PublishThinkingAsync(
                context,
                task,
                "rag_query_started",
                "AI is reading knowledge",
                "AI is searching account knowledge, platform guidance, content formulas, and visual references.",
                new
                {
                    socialMediaId = msg.SocialMediaId,
                    query = recommendationQuery,
                    topK = msg.TopK,
                    precomputedRewrite = new
                    {
                        language = rewrite.Language,
                        intent = rewrite.Intent,
                        primaryQuery = rewrite.PrimaryQuery,
                        altQueries = rewrite.AltQueries,
                        visualQuery = rewrite.VisualQuery,
                        keyTerms = rewrite.KeyTerms,
                    },
                },
                ct);
            await PublishThinkingAsync(
                context,
                task,
                "web_search_started",
                "AI is searching the web",
                "AI is checking fresh web context for current trends and timely facts.",
                new
                {
                    query = recommendationQuery,
                    primaryQuery = rewrite.PrimaryQuery,
                    altQueries = rewrite.AltQueries,
                    visualQuery = rewrite.VisualQuery,
                    keyTerms = rewrite.KeyTerms,
                    provider = "recommendation-llm-web-search",
                    reason = isAutoTopic
                        ? "Auto-discovery needs fresh current context before choosing the topic."
                        : "Recommendation generation can use fresh current context when useful.",
                },
                ct);
            var queryResult = await _mediator.Send(
                new QueryAccountRecommendationsQuery(
                    msg.UserId, msg.SocialMediaId, recommendationQuery, msg.TopK,
                    PrecomputedRewrite: rewrite), ct);
            if (queryResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"RAG query failed: {queryResult.Error.Code} {queryResult.Error.Description}");
            }
            var rag = queryResult.Value;
            await PublishThinkingAsync(
                context,
                task,
                "web_search_completed",
                "AI searched the web",
                "AI finished checking fresh web context.",
                new
                {
                    query = recommendationQuery,
                    sourceCount = rag.WebSources?.Count ?? 0,
                    webSources = rag.WebSources,
                },
                ct,
                phaseStatus: "completed");

            // Prefer the S3-mirrored URL (OpenAI / OpenRouter can fetch it) over the
            // raw FB CDN URL (which they refuse — same robots.txt issue Vertex hits).
            //
            // We deliberately keep MORE candidates here than the per-draft cap so the
            // reranker (Step 3.4) has a real selection to make. Final cap is applied
            // after rerank scoring; everything below the relevance threshold is dropped
            // regardless of how few refs that leaves.
            // Build the candidate pool. Each ref contributes:
            //   1. Its static image (thumbnail / post image) if present
            //   2. Up to N video frame URLs from its matched segments (frame-level
            //      Qdrant ingest surfaces these — the highest-scoring frame within
            //      each surviving segment is what we get here)
            // A video post can therefore contribute up to ~3 distinct candidates:
            // the thumbnail + 2 segment best-frames (per the segment-rerank cap).
            var pastPostCandidates = new List<ImageRefCandidate>();
            foreach (var r in rag.References)
            {
                if (pastPostCandidates.Count >= DefaultRerankCandidatePool) break;

                var staticUrl = r.MirroredImageUrl ?? r.ImageUrl;
                if (!string.IsNullOrWhiteSpace(staticUrl))
                {
                    pastPostCandidates.Add(new ImageRefCandidate(
                        ImageUrl: staticUrl!,
                        Source: "past-post",
                        DescriptiveText: BuildPastPostCandidateText(r)));
                    if (pastPostCandidates.Count >= DefaultRerankCandidatePool) break;
                }

                if (r.VideoFrameUrls is { Count: > 0 })
                {
                    foreach (var frameUrl in r.VideoFrameUrls)
                    {
                        if (string.IsNullOrWhiteSpace(frameUrl)) continue;
                        pastPostCandidates.Add(new ImageRefCandidate(
                            ImageUrl: frameUrl,
                            Source: "past-post-video-frame",
                            DescriptiveText: BuildVideoFrameCandidateText(r, frameUrl)));
                        if (pastPostCandidates.Count >= DefaultRerankCandidatePool) break;
                    }
                }
            }

            // For backward-compat with the rest of this method, keep `topImageUrls`
            // referring to the past-post slice (unranked). Final reranked list is
            // computed in Step 3.4 below as `imageBriefRefImageUrls`.
            var topImageUrls = pastPostCandidates.Select(c => c.ImageUrl).ToList();

            _logger.LogInformation(
                "DraftPost {Id}: RAG returned answer={AnswerLen} chars, references={RefCount} (with images={WithImageCount}), webSources={SourceCount}",
                task.Id,
                (rag.Answer ?? string.Empty).Length,
                rag.References.Count,
                topImageUrls.Count,
                rag.WebSources?.Count ?? 0);
            _logger.LogInformation(
                "DraftPost {Id}: rag.Answer (passed downstream as context):\n{Answer}",
                task.Id, Truncate(rag.Answer ?? string.Empty, 4000));
            for (var i = 0; i < topImageUrls.Count; i++)
            {
                _logger.LogInformation(
                    "DraftPost {Id}: refImage[{Idx}] = {Url}",
                    task.Id, i, topImageUrls[i][..Math.Min(topImageUrls[i].Length, 120)]);
            }
            await PublishThinkingAsync(
                context,
                task,
                "rag_query_completed",
                "AI read knowledge",
                "AI finished reading account knowledge and selected references.",
                new
                {
                    documentIdPrefix = rag.DocumentIdPrefix,
                    answer = rag.Answer,
                    pageProfileText = rag.PageProfileText,
                    references = rag.References,
                    webSources = rag.WebSources,
                    selectedPastPostImageUrls = topImageUrls,
                },
                ct,
                phaseStatus: "completed");

            // Step 3 — caption generation (gpt-4o-mini multimodal). The caption system
            // prompt is style-aware: creative omits contact info entirely, branded
            // surfaces it when warranted, marketing requires the full contact block.
            //
            // In auto-topic mode the user's "topic" is whatever the recommendation LLM
            // chose — we point the caption LLM at the recommendation summary as the
            // source of truth for the topic, since the entity's UserPrompt is just a
            // placeholder marker.
            //
            // Caption gets the per-draft cap (msg.MaxReferenceImages) worth of past-post
            // images by their original RAG rank — rerank hasn't run yet at this stage,
            // and the caption LLM doesn't need every candidate; it only uses refs to
            // anchor voice/style, not subject.
            var captionSystemPrompt = CaptionSystemPromptFor(style);
            var topicForDownstream = isAutoTopic
                ? "(Auto-discovered topic — read the recommendation summary above; the chosen topic is stated at the top of that summary.)"
                : msg.UserPrompt;
            var captionRefImageUrls = topImageUrls.Take(msg.MaxReferenceImages).ToList();
            var captionUserText = BuildCaptionUserText(topicForDownstream, rag, captionRefImageUrls.Count, style);
            _logger.LogInformation(
                "LLM[caption] INPUT for DraftPost {Id} Style={Style} ({UserTextLen} chars, {RefCount} ref images):\n{UserText}",
                task.Id, style, captionUserText.Length, captionRefImageUrls.Count, Truncate(captionUserText, 4000));
            await PublishThinkingAsync(
                context,
                task,
                "caption_generation_started",
                "AI is writing the caption",
                "AI is writing the recommendation caption using the retrieved knowledge and reference images.",
                new
                {
                    style,
                    topic = topicForDownstream,
                    systemPrompt = captionSystemPrompt,
                    userText = captionUserText,
                    referenceImageUrls = captionRefImageUrls,
                },
                ct);
            var captionResult = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: captionSystemPrompt,
                    UserText: captionUserText,
                    ReferenceImageUrls: captionRefImageUrls),
                ct);
            // Web sources from search-preview model are intentionally discarded for the
            // caption — captions go straight into the post and shouldn't carry inline
            // URL citations. The recommendation step (rag.Answer) already surfaces
            // sources separately if the user wants to see them.
            var caption = (captionResult.Answer ?? string.Empty).Trim().Trim('"');
            _logger.LogInformation(
                "LLM[caption] OUTPUT for DraftPost {Id} ({CaptionLen} chars):\n{Caption}",
                task.Id, caption.Length, Truncate(caption, 2000));
            if (captionResult.Sources.Count > 0)
            {
                _logger.LogInformation(
                    "DraftPost {Id}: caption model cited {Count} web source(s) (discarded for caption text)",
                    task.Id, captionResult.Sources.Count);
            }
            if (string.IsNullOrWhiteSpace(caption))
            {
                throw new InvalidOperationException("Caption generation returned empty content.");
            }
            await PublishThinkingAsync(
                context,
                task,
                "caption_generation_completed",
                "AI wrote the caption",
                "AI finished writing the recommendation caption.",
                new
                {
                    caption,
                    sources = captionResult.Sources,
                    discardedSourceCount = captionResult.Sources.Count,
                },
                ct,
                phaseStatus: "completed");

            // Step 3.3 — fetch FRESH real-world reference images for the chosen topic.
            // Past-post images anchor visual STYLE / palette; fresh-topic images
            // anchor SUBJECT. Both feed into the same rerank pool below.
            //
            // Query is the user's topic if explicit, or the auto-discovery LLM's chosen
            // topic line if not. Brave's image search runs ~$0.003 per call.
            var refImageQuery = ExtractRefImageQuery(msg.UserPrompt, isAutoTopic, rag.Answer);
            await PublishThinkingAsync(
                context,
                task,
                "fresh_image_search_started",
                "AI is finding visual references",
                "AI is searching for fresh real-world image references for the chosen topic.",
                new
                {
                    query = refImageQuery,
                    source = "brave-image-search",
                },
                ct);
            var freshRefImageHits = await FetchFreshTopicImageHitsAsync(refImageQuery, ct);
            _logger.LogInformation(
                "DraftPost {Id}: fresh-ref-image search query=\"{Query}\" → {Count} hit(s)",
                task.Id, refImageQuery ?? "(empty)", freshRefImageHits.Count);
            var freshTopicCandidates = freshRefImageHits
                .Select(h => new ImageRefCandidate(
                    ImageUrl: h.ImageUrl,
                    Source: "fresh-topic",
                    DescriptiveText: BuildFreshTopicCandidateText(h, refImageQuery)))
                .ToList();
            for (var i = 0; i < freshTopicCandidates.Count; i++)
            {
                _logger.LogInformation(
                    "DraftPost {Id}: freshRefImage[{Idx}] = {Url}",
                    task.Id, i,
                    freshTopicCandidates[i].ImageUrl[..Math.Min(freshTopicCandidates[i].ImageUrl.Length, 200)]);
            }

            // Step 3.35 — RERANK candidate pool (past-post + fresh-topic) against the
            // topic + caption, keeping only the truly relevant ones up to the per-draft
            // cap. Threshold-gated: a draft with no relevant candidates simply gets fewer
            // refs (or zero) rather than the previous behavior of forwarding noise.
            await PublishThinkingAsync(
                context,
                task,
                "fresh_image_search_completed",
                "AI found visual references",
                "AI finished searching for fresh image references.",
                new
                {
                    query = refImageQuery,
                    hits = freshRefImageHits,
                    candidateUrls = freshTopicCandidates.Select(candidate => candidate.ImageUrl).ToList(),
                    candidates = freshTopicCandidates.Select(candidate => new
                    {
                        candidate.ImageUrl,
                        candidate.Source,
                        candidate.DescriptiveText,
                    }).ToList(),
                },
                ct,
                phaseStatus: "completed");
            var rerankCandidates = pastPostCandidates.Concat(freshTopicCandidates).ToList();
            await PublishThinkingAsync(
                context,
                task,
                "reference_rerank_started",
                "AI is choosing reference images",
                "AI is ranking past-post and fresh-topic images against the caption and topic.",
                new
                {
                    topic = refImageQuery,
                    caption,
                    visualQuery = rewrite.VisualQuery,
                    keyTerms = rewrite.KeyTerms,
                    cap = msg.MaxReferenceImages,
                    candidateCount = rerankCandidates.Count,
                    candidates = rerankCandidates.Select(candidate => new
                    {
                        candidate.ImageUrl,
                        candidate.Source,
                        candidate.DescriptiveText,
                    }).ToList(),
                },
                ct);
            var imageBriefRefImageUrls = await SelectReferenceImagesAsync(
                taskId: task.Id,
                candidates: rerankCandidates,
                topic: refImageQuery,
                caption: caption,
                visualQuery: rewrite.VisualQuery,                  // ← K4 enhancement
                keyTerms: rewrite.KeyTerms,                        // ← K4 enhancement
                cap: msg.MaxReferenceImages,
                cancellationToken: ct);
            await PublishThinkingAsync(
                context,
                task,
                "reference_rerank_completed",
                "AI chose reference images",
                "AI selected the images that best match the draft topic and caption.",
                new
                {
                    selectedReferenceImageUrls = imageBriefRefImageUrls,
                    selectedCount = imageBriefRefImageUrls.Count,
                    candidateCount = rerankCandidates.Count,
                    cap = msg.MaxReferenceImages,
                },
                ct,
                phaseStatus: "completed");
            // Surface freshRefImageUrls for downstream logs that distinguish the two sources.
            var freshRefImageUrls = freshTopicCandidates.Select(c => c.ImageUrl).ToList();

            // Step 3.4 — fetch style-specific design rules from the knowledge base.
            // Each style maps 1:1 to a knowledge namespace (knowledge:image-design-{style}:)
            // bootstrapped at rag-microservice startup from service/knowledge/*.md.
            var styleKnowledgeDocumentIdPrefix = $"knowledge:image-design-{style}:";
            await PublishThinkingAsync(
                context,
                task,
                "style_knowledge_started",
                "AI is reading style knowledge",
                $"AI is reading {style} image-design knowledge from RAG.",
                new
                {
                    style,
                    language = rewrite.Language,
                    documentIdPrefix = styleKnowledgeDocumentIdPrefix,
                },
                ct);
            var styleKnowledge = await FetchStyleKnowledgeAsync(style, rewrite.Language, ct);
            _logger.LogInformation(
                "DraftPost {Id}: style-knowledge[{Style}] fetched ({Len} chars)",
                task.Id, style, styleKnowledge.Length);
            if (styleKnowledge.Length > 0)
            {
                _logger.LogInformation(
                    "DraftPost {Id}: style-knowledge[{Style}]:\n{Knowledge}",
                    task.Id, style, Truncate(styleKnowledge, 4000));
            }

            // Step 3.5 — LLM-driven image brief. The image-gen model can't read RAG,
            // see videos, or follow long instructions; gpt-4o-mini does that work for
            // it and synthesizes a focused brief (subject + composition + palette +
            // platform-correct aspect ratio + style-specific text-overlay rules).
            _logger.LogInformation(
                "DraftPost {Id}: building image brief (caption={CaptionLen} chars, pastPostRefs={PastCount}, freshTopicRefs={FreshCount}, style={Style})",
                task.Id, caption.Length, topImageUrls.Count, freshRefImageUrls.Count, style);
            await PublishThinkingAsync(
                context,
                task,
                "style_knowledge_completed",
                "AI read style knowledge",
                $"AI finished reading {style} image-design knowledge.",
                new
                {
                    style,
                    language = rewrite.Language,
                    documentIdPrefix = styleKnowledgeDocumentIdPrefix,
                    knowledge = styleKnowledge,
                },
                ct,
                phaseStatus: "completed");
            await PublishThinkingAsync(
                context,
                task,
                "image_brief_generation_started",
                "AI is planning the image",
                "AI is turning the caption, RAG answer, style knowledge, and reference images into an image brief.",
                new
                {
                    style,
                    topic = topicForDownstream,
                    caption,
                    ragAnswer = rag.Answer,
                    styleKnowledge,
                    referenceImageUrls = imageBriefRefImageUrls,
                },
                ct);
            var brief = await BuildImageBriefAsync(
                userPrompt: topicForDownstream,
                caption: caption,
                rag: rag,
                topImageUrls: imageBriefRefImageUrls,
                style: style,
                styleKnowledge: styleKnowledge,
                cancellationToken: ct);
            _logger.LogInformation(
                "LLM[imageBrief] OUTPUT for DraftPost {Id} Style={Style}: aspect={AspectRatio}, prompt={PromptLen} chars, styleNotes={StyleLen} chars",
                task.Id, style, brief.AspectRatio, brief.Prompt.Length, brief.StyleNotes?.Length ?? 0);
            _logger.LogInformation(
                "LLM[imageBrief] PROMPT for DraftPost {Id}:\n{Prompt}",
                task.Id, Truncate(brief.Prompt, 2500));
            if (!string.IsNullOrWhiteSpace(brief.StyleNotes))
            {
                _logger.LogInformation(
                    "LLM[imageBrief] STYLE_NOTES for DraftPost {Id}:\n{StyleNotes}",
                    task.Id, Truncate(brief.StyleNotes, 1500));
            }

            // Step 4 — image generation (gpt-5.4-image-2 multimodal). The image-gen
            // prompt is the LLM-authored brief; the system prompt is style-aware
            // (creative = no text on image, branded = optional headline, marketing =
            // full quoted-text rendering) plus the brief's style_notes.
            await PublishThinkingAsync(
                context,
                task,
                "image_brief_generation_completed",
                "AI planned the image",
                "AI finished the image-generation brief.",
                new
                {
                    prompt = brief.Prompt,
                    brief.AspectRatio,
                    brief.StyleNotes,
                    referenceImageUrls = imageBriefRefImageUrls,
                },
                ct,
                phaseStatus: "completed");
            var imageBaseSystem = ImageSystemPromptFor(style);
            var fullImagePrompt =
                $"{brief.Prompt}\n\n" +
                $"Aspect ratio: {brief.AspectRatio}. " +
                "Render at high resolution. Output an image, no text response.";
            var fullImageSystemPrompt = string.IsNullOrWhiteSpace(brief.StyleNotes)
                ? imageBaseSystem
                : $"{imageBaseSystem}\n\nAdditional style constraints from the art-director brief: {brief.StyleNotes}";
            _logger.LogInformation(
                "IMAGEGEN INPUT for DraftPost {Id} Style={Style} ({RefCount} ref images = {PastCount} past-post + {FreshCount} fresh-topic):\n  --- prompt ---\n{Prompt}\n  --- system ---\n{System}",
                task.Id, style, imageBriefRefImageUrls.Count, topImageUrls.Count, freshRefImageUrls.Count,
                Truncate(fullImagePrompt, 2500),
                Truncate(fullImageSystemPrompt, 1500));
            await PublishThinkingAsync(
                context,
                task,
                "image_generation_started",
                "AI is generating the image",
                "AI is generating the draft image from the brief and selected references.",
                new
                {
                    style,
                    prompt = fullImagePrompt,
                    systemPrompt = fullImageSystemPrompt,
                    referenceImageUrls = imageBriefRefImageUrls,
                },
                ct);
            var imageResult = await _imageGenClient.GenerateImageAsync(
                new ImageGenerationRequest(
                    Prompt: fullImagePrompt,
                    ReferenceImageUrls: imageBriefRefImageUrls,
                    SystemPrompt: fullImageSystemPrompt),
                ct);
            _logger.LogInformation(
                "IMAGEGEN OUTPUT for DraftPost {Id}: mime={MimeType}, dataUrlLen={Len}, promptTokens={Pt}, completionTokens={Ct}, costUsd={Cost}",
                task.Id, imageResult.MimeType, imageResult.DataUrl.Length,
                imageResult.PromptTokens, imageResult.CompletionTokens, imageResult.CostUsd);
            await PublishThinkingAsync(
                context,
                task,
                "image_generation_completed",
                "AI generated the image",
                "AI finished generating the draft image.",
                new
                {
                    imageResult.MimeType,
                    dataUrlLength = imageResult.DataUrl.Length,
                    imageResult.PromptTokens,
                    imageResult.CompletionTokens,
                    imageResult.CostUsd,
                },
                ct,
                phaseStatus: "completed");

            // Step 5 — upload generated image to S3. The User microservice's
            // CreateResourcesFromUrlsAsync handles `data:` URLs by decoding base64 server-side.
            _logger.LogDebug("DraftPost {Id}: uploading generated image to S3...", task.Id);
            await PublishThinkingAsync(
                context,
                task,
                "resource_upload_started",
                "AI is saving the image",
                "AI is uploading the generated image to workspace storage.",
                new
                {
                    workspaceId = msg.WorkspaceId,
                    resourceType = "image",
                    status = "generated",
                    contentType = imageResult.MimeType,
                    originKind = ResourceOriginKinds.AiGenerated,
                },
                ct);
            var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                userId: msg.UserId,
                urls: new[] { imageResult.DataUrl },
                status: "generated",
                resourceType: "image",
                cancellationToken: ct,
                workspaceId: msg.WorkspaceId,
                provenance: new ResourceProvenanceMetadata(
                    OriginKind: ResourceOriginKinds.AiGenerated,
                    OriginChatSessionId: null,
                    OriginChatId: null));

            if (uploadResult.IsFailure || uploadResult.Value.Count == 0)
            {
                throw new InvalidOperationException(
                    $"S3 upload failed: {uploadResult.Error?.Code} {uploadResult.Error?.Description}");
            }
            var uploaded = uploadResult.Value[0];
            await PublishThinkingAsync(
                context,
                task,
                "resource_upload_completed",
                "AI saved the image",
                "AI finished uploading the generated image.",
                new
                {
                    uploaded.ResourceId,
                    uploaded.PresignedUrl,
                    uploaded.ContentType,
                    uploaded.ResourceType,
                    uploaded.OriginKind,
                    uploaded.OriginSourceUrl,
                    uploaded.OriginChatSessionId,
                    uploaded.OriginChatId,
                },
                ct,
                phaseStatus: "completed");

            // Step 6 — populate the draft Post with the generated caption + image.
            //
            // The Post row was created EMPTY by StartDraftPostGenerationCommandHandler
            // at submit time (so the 202 response could return a real postId for FE).
            // We update it in place rather than inserting a new row — preserves the id
            // the FE may already be polling.
            //
            // Legacy fallback: tasks queued before this change have no ResultPostId
            // set; for those we keep the old behavior (create a fresh standalone Post).
            await PublishThinkingAsync(
                context,
                task,
                "draft_post_finalizing_started",
                "AI is finalizing the draft",
                "AI is saving the generated caption and image on the draft post.",
                new
                {
                    draftPostId = task.ResultPostId,
                    hasPrecreatedDraftPost = task.ResultPostId.HasValue,
                    resourceId = uploaded.ResourceId,
                    caption,
                },
                ct);
            var content = new PostContent
            {
                Content = caption,
                ResourceList = new List<string> { uploaded.ResourceId.ToString() },
                PostType = "posts",
            };
            Post draftPost;
            if (task.ResultPostId.HasValue)
            {
                _logger.LogDebug(
                    "DraftPost {Id}: updating pre-created draft Post {PostId}...",
                    task.Id, task.ResultPostId.Value);
                draftPost = await _postRepository.GetByIdForUpdateAsync(task.ResultPostId.Value, ct)
                    ?? throw new InvalidOperationException(
                        $"Pre-created draft post {task.ResultPostId.Value} disappeared before consumer finalize");
                draftPost.Content = content;
                draftPost.Status = "draft";
                draftPost.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            }
            else
            {
                _logger.LogDebug(
                    "DraftPost {Id}: ResultPostId not set (legacy task) — creating fresh standalone Post",
                    task.Id);
                draftPost = new Post
                {
                    Id = Guid.CreateVersion7(),
                    UserId = msg.UserId,
                    WorkspaceId = msg.WorkspaceId,
                    ChatSessionId = null,
                    SocialMediaId = msg.SocialMediaId,
                    PostBuilderId = null,
                    Platform = null,
                    Title = null,
                    Content = content,
                    Status = "draft",
                    CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
                    UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow,
                };
                await _postRepository.AddAsync(draftPost, ct);
            }
            await _postRepository.SaveChangesAsync(ct);
            postFinalized = true;
            task.ResultPostId = draftPost.Id;
            await PublishThinkingAsync(
                context,
                task,
                "draft_post_finalized",
                "AI finalized the draft",
                "AI saved the generated caption and image on the draft post.",
                new
                {
                    draftPostId = draftPost.Id,
                    resourceId = uploaded.ResourceId,
                    presignedUrl = uploaded.PresignedUrl,
                    caption,
                },
                ct,
                phaseStatus: "completed");

            // Step 7 — mark task completed + notify
            task.Status = DraftPostTaskStatuses.Completed;
            task.ResultPostBuilderId = null;
            task.ResultPostId = draftPost.Id;
            task.ResultResourceId = uploaded.ResourceId;
            task.ResultPresignedUrl = uploaded.PresignedUrl;
            task.ResultCaption = caption;
            task.ResultReferencesJson = SerializeReferences(rag.References);
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            await _taskRepository.SaveChangesAsync(ct);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    msg.UserId,
                    NotificationTypes.AiDraftPostGenerationCompleted,
                    "Draft post is ready",
                    "Your AI-generated draft post (caption + image) is ready.",
                    new
                    {
                        correlationId = task.CorrelationId,
                        socialMediaId = task.SocialMediaId,
                        draftPostId = task.ResultPostId,
                        postId = task.ResultPostId,
                        resourceId = task.ResultResourceId,
                        presignedUrl = task.ResultPresignedUrl,
                        caption = task.ResultCaption,
                    },
                    createdAt: task.CompletedAt,
                    source: NotificationSourceConstants.Creator),
                ct);

            _logger.LogInformation(
                "DraftPost {Id}: completed CorrelationId={CorrelationId} PostId={PostId} ResourceId={ResourceId}",
                task.Id, task.CorrelationId, task.ResultPostId, task.ResultResourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DraftPost {Id}: failed CorrelationId={CorrelationId}", task.Id, task.CorrelationId);
            var failedDraftPostId = task.ResultPostId;

            // Clean up the upfront empty Post if processing failed BEFORE Step 6
            // finalized it. After finalization the Post has real caption + image
            // and we keep it. Best-effort: don't let cleanup errors mask the
            // original failure.
            if (!postFinalized && task.ResultPostId.HasValue)
            {
                try
                {
                    var emptyPost = await _postRepository.GetByIdForUpdateAsync(task.ResultPostId.Value, ct);
                    if (emptyPost != null && emptyPost.DeletedAt is null)
                    {
                        emptyPost.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
                        emptyPost.UpdatedAt = emptyPost.DeletedAt;
                        await _postRepository.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "DraftPost {Id}: soft-deleted empty placeholder Post {PostId} on failure",
                            task.Id, emptyPost.Id);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx,
                        "DraftPost {Id}: failed to soft-delete empty placeholder Post {PostId}",
                        task.Id, task.ResultPostId.Value);
                }
            }

            task.Status = DraftPostTaskStatuses.Failed;
            task.ErrorCode = ex.GetType().Name;
            task.ErrorMessage = ex.Message;
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            // Clear ResultPostId so the response doesn't dangle a soft-deleted
            // placeholder id back to the FE on Failed status.
            if (!postFinalized)
            {
                task.ResultPostId = null;
            }
            try
            {
                await _taskRepository.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "DraftPost {Id}: failed to persist Failed status", task.Id);
            }

            try
            {
                await context.Publish(
                    NotificationRequestedEventFactory.CreateForUser(
                        msg.UserId,
                        NotificationTypes.AiDraftPostGenerationFailed,
                        "Draft post generation failed",
                        "Your AI draft post could not be generated. Please try again.",
                        new
                        {
                            correlationId = task.CorrelationId,
                            socialMediaId = task.SocialMediaId,
                            draftPostId = failedDraftPostId,
                            postId = failedDraftPostId,
                            errorCode = task.ErrorCode,
                            errorMessage = task.ErrorMessage,
                        },
                        createdAt: task.CompletedAt,
                        source: NotificationSourceConstants.Creator),
                    ct);
            }
            catch (Exception notifyEx)
            {
                _logger.LogError(notifyEx, "DraftPost {Id}: failed to publish failure notification", task.Id);
            }
        }
    }

    private static string BuildCaptionUserText(
        string userPrompt,
        Application.Recommendations.Queries.AccountRecommendationsAnswer rag,
        int attachedImageCount,
        string style)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"User's topic for the next post: {userPrompt}");
        sb.AppendLine($"Requested post STYLE: {style}");
        sb.AppendLine();
        // Page profile, verbatim. This is the single source of truth for the page's
        // website / email / phone — the caption LLM must copy these EXACTLY (no
        // paraphrasing). Without this dedicated block, the caption LLM would only
        // see the recommendation summary and would reflexively invent canonical-
        // looking contact strings (we observed `meai.website` → `meai.com`).
        if (!string.IsNullOrWhiteSpace(rag.PageProfileText))
        {
            sb.AppendLine("=== Page profile (verbatim — single source of truth for contact info) ===");
            sb.AppendLine("Use these values EXACTLY as written. Do NOT paraphrase. Do NOT invent variants.");
            sb.AppendLine("Omit any field NOT present here — never fabricate a placeholder.");
            sb.AppendLine(rag.PageProfileText);
            sb.AppendLine("=== End of page profile ===");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(rag.Answer))
        {
            sb.AppendLine("Retrieved RAG recommendation summary:");
            sb.AppendLine(rag.Answer);
            sb.AppendLine();
        }
        var captionSamples = rag.References
            .Where(r => !string.IsNullOrWhiteSpace(r.Caption))
            .Take(6)
            .ToList();
        if (captionSamples.Count > 0)
        {
            sb.AppendLine("Recent past captions from this account (for voice/style):");
            for (var i = 0; i < captionSamples.Count; i++)
            {
                var r = captionSamples[i];
                var snippet = (r.Caption ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
                if (snippet.Length > 240) snippet = snippet[..240] + "...";
                sb.AppendLine($"[{i + 1}] postId={r.PostId} caption=\"{snippet}\"");
            }
            sb.AppendLine();
        }
        if (attachedImageCount > 0)
        {
            sb.AppendLine($"The next {attachedImageCount} attached image(s) are reference images from past posts. Use them as visual context.");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Caps how many fresh real-world topic images we attach to the image-brief +
    /// image-gen calls. Two is the sweet spot — enough variety for the model to
    /// triangulate the subject, but not so many that we drown the brand's past-post
    /// style references.
    /// </summary>
    private const int MaxFreshTopicImages = 2;

    /// <summary>
    /// Decides what to send to Brave's image search. We want a tight noun-phrase
    /// describing the SUBJECT of the post — not a verbose user prompt and not the
    /// whole recommendation summary.
    ///
    /// Priority:
    ///   1. User-supplied prompt — strip common preamble like "create content about"
    ///      / "i want content about" / "write a post on" so the query is just the
    ///      noun phrase.
    ///   2. Auto-discovery — extract the topic from the recommendation summary by
    ///      regex-matching for the "Chosen Topic: …" line the system prompt instructs
    ///      the LLM to emit; fall back to the first non-decorative line.
    ///   3. Failing both — return null (search will be skipped).
    /// </summary>
    private static string? ExtractRefImageQuery(string? userPrompt, bool isAutoTopic, string? ragAnswer)
    {
        if (!isAutoTopic && !string.IsNullOrWhiteSpace(userPrompt))
        {
            var cleaned = StripPreambleVerbs(userPrompt!.Trim());
            if (cleaned.Length > 0)
            {
                return cleaned.Length > 80 ? cleaned[..80] : cleaned;
            }
        }

        if (!string.IsNullOrWhiteSpace(ragAnswer))
        {
            // Look for "Chosen Topic: <X>" — the auto-discovery system prompt asks the
            // LLM to label the chosen topic this way at the top of its answer. Match
            // is case-insensitive; tolerates Markdown markers (### / ** / etc.) before.
            var topicMatch = Regex.Match(
                ragAnswer,
                @"chosen\s*topic\s*[:\-]\s*(?<topic>[^\r\n]+)",
                RegexOptions.IgnoreCase);
            if (topicMatch.Success)
            {
                var topic = StripMarkdownInline(topicMatch.Groups["topic"].Value).Trim().Trim('.', '!', '?');
                if (topic.Length > 0)
                {
                    return topic.Length > 80 ? topic[..80] : topic;
                }
            }

            // Fallback: first content line, stripped of decoration.
            foreach (var rawLine in ragAnswer.Split('\n'))
            {
                var line = StripMarkdownInline(rawLine).Trim();
                if (line.Length < 4) continue;
                // Skip obvious headings/decoration that aren't content.
                if (line.StartsWith("---")) continue;
                return line.Length > 80 ? line[..80] : line;
            }
        }

        return null;
    }

    private static string StripPreambleVerbs(string s)
    {
        // Drop common conversational openings so "create content about DJI Osmo" → "DJI Osmo".
        var patterns = new[]
        {
            @"^\s*(please\s+)?(create|generate|make|write|draft)\s+(a\s+)?(post|content|article|caption|piece)?\s*(about|on|for|regarding|of)\s+",
            @"^\s*i\s+(want|need|would\s+like)\s+(a\s+)?(post|content|article|caption|piece)?\s*(about|on|for|regarding|of)\s+",
            @"^\s*tell\s+me\s+(a\s+)?(post|content|article|caption|piece)?\s*(about|on|for|regarding|of)\s+",
        };
        foreach (var p in patterns)
        {
            var m = Regex.Match(s, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return s[m.Length..].Trim();
            }
        }
        return s;
    }

    private static string StripMarkdownInline(string s)
    {
        // Lightweight: drop heading hashes, leading/trailing asterisks, leading bullets,
        // and emoji-only prefixes so the result reads as plain text.
        s = Regex.Replace(s, @"^[\s#>*_\-•·]+", "");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
        s = Regex.Replace(s, @"__(.+?)__", "$1");
        s = Regex.Replace(s, @"`([^`]+)`", "$1");
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        return s;
    }

    /// <summary>
    /// Fires Brave image search and returns up to <see cref="MaxFreshTopicImages"/>
    /// hits (with title/source URL preserved so the rerank step has descriptive text
    /// to score against). Returns empty on null/blank query, no API key configured,
    /// or a transport error — never throws (a search failure must not drop the draft).
    /// </summary>
    private async Task<IReadOnlyList<Application.Abstractions.Search.ImageSearchHit>> FetchFreshTopicImageHitsAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<Application.Abstractions.Search.ImageSearchHit>();
        }

        try
        {
            var hits = await _imageSearchClient.SearchImagesAsync(
                query, MaxFreshTopicImages, cancellationToken);
            return hits
                .Where(h => !string.IsNullOrWhiteSpace(h.ImageUrl))
                .Take(MaxFreshTopicImages)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fresh-topic image search failed for query='{Query}'", query);
            return Array.Empty<Application.Abstractions.Search.ImageSearchHit>();
        }
    }

    /// <summary>
    /// One image-reference candidate going into the rerank pool. <see cref="DescriptiveText"/>
    /// is what the reranker scores against the query — for past posts that's the post
    /// caption, for fresh-topic hits that's the search-result title.
    /// </summary>
    private sealed record ImageRefCandidate(
        string ImageUrl,
        string Source,
        string DescriptiveText);

    private static string BuildPastPostCandidateText(
        Application.Recommendations.Queries.RecommendationReference r)
    {
        var parts = new List<string>();
        parts.Add($"Past post (postId={r.PostId ?? "n/a"})");
        if (!string.IsNullOrWhiteSpace(r.Caption))
        {
            var caption = r.Caption!.Replace('\n', ' ').Replace('\r', ' ');
            if (caption.Length > 240) caption = caption[..240] + "…";
            parts.Add("caption: \"" + caption + "\"");
        }
        if (!string.IsNullOrWhiteSpace(r.VideoTranscript))
        {
            var t = r.VideoTranscript!.Replace('\n', ' ').Replace('\r', ' ');
            if (t.Length > 200) t = t[..200] + "…";
            parts.Add("video segment transcript: \"" + t + "\"");
        }
        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Descriptive text for an extracted video frame in the rerank pool. Pairs
    /// the URL with the post's caption + transcript context so the reranker can
    /// score the frame against the topic. Frames are scored visually too — the
    /// text label here is mainly for log readability.
    /// </summary>
    private static string BuildVideoFrameCandidateText(
        Application.Recommendations.Queries.RecommendationReference r,
        string frameUrl)
    {
        var parts = new List<string>
        {
            $"Video frame from past post (postId={r.PostId ?? "n/a"})",
        };
        if (!string.IsNullOrWhiteSpace(r.VideoSegmentTime))
        {
            parts.Add($"segment time={r.VideoSegmentTime}");
        }
        if (!string.IsNullOrWhiteSpace(r.Caption))
        {
            var caption = r.Caption!.Replace('\n', ' ').Replace('\r', ' ');
            if (caption.Length > 200) caption = caption[..200] + "…";
            parts.Add("caption: \"" + caption + "\"");
        }
        if (!string.IsNullOrWhiteSpace(r.VideoTranscript))
        {
            var t = r.VideoTranscript!.Replace('\n', ' ').Replace('\r', ' ');
            if (t.Length > 160) t = t[..160] + "…";
            parts.Add("transcript: \"" + t + "\"");
        }
        return string.Join(" | ", parts);
    }

    private static string BuildFreshTopicCandidateText(
        Application.Abstractions.Search.ImageSearchHit h,
        string? topicQuery)
    {
        var parts = new List<string> { "Fresh image search result" };
        if (!string.IsNullOrWhiteSpace(topicQuery))
        {
            parts.Add($"for query \"{topicQuery}\"");
        }
        if (!string.IsNullOrWhiteSpace(h.Title))
        {
            var t = h.Title!.Replace('\n', ' ').Replace('\r', ' ');
            if (t.Length > 240) t = t[..240] + "…";
            parts.Add("title: \"" + t + "\"");
        }
        if (!string.IsNullOrWhiteSpace(h.SourcePageUrl))
        {
            parts.Add($"source: {h.SourcePageUrl}");
        }
        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Final image-ref selection: rerank the candidate pool against the topic+caption
    /// query, drop anything below <see cref="RerankRelevanceThreshold"/>, sort by score,
    /// cap at the per-draft request limit. Returns image URLs in rerank-score order.
    ///
    /// Failure mode: if the reranker returns nothing (no key, transport error, etc.)
    /// we fall back to the original RAG ordering, capped — so reranker outage degrades
    /// gracefully rather than dropping the draft.
    /// </summary>
    private async Task<List<string>> SelectReferenceImagesAsync(
        Guid taskId,
        IReadOnlyList<ImageRefCandidate> candidates,
        string? topic,
        string caption,
        string? visualQuery,                    // ← K4 enhancement: rewriter's visual_query
        IReadOnlyList<string>? keyTerms,        // ← K4 enhancement: rewriter's key_terms
        int cap,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            _logger.LogInformation("DraftPost {Id}: rerank skipped — empty candidate pool", taskId);
            return new List<string>();
        }

        // Compose the rerank query. Topic alone is too thin; the caption gives the
        // reranker the actual content the post is about, so it can match e.g. "smartphone
        // gimbal" candidates even when the user prompt was just "DJI Osmo".
        // Plus visual_query (English visually-descriptive) gives the cross-encoder
        // anchors specific to the IMAGE rather than the text — Jina-m0 scores against
        // pixel content, so visual nouns dominate.
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(topic)) queryParts.Add($"Topic: {topic}");
        if (!string.IsNullOrWhiteSpace(visualQuery)) queryParts.Add($"Visual: {visualQuery}");
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var captionForQuery = caption.Replace('\n', ' ').Replace('\r', ' ');
            if (captionForQuery.Length > 800) captionForQuery = captionForQuery[..800] + "…";
            queryParts.Add($"Caption: {captionForQuery}");
        }
        if (keyTerms is { Count: > 0 })
        {
            queryParts.Add("Key terms: " + string.Join(", ", keyTerms.Take(6)));
        }
        var query = string.Join("\n", queryParts);

        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogInformation(
                "DraftPost {Id}: rerank skipped — empty query; falling back to candidate order",
                taskId);
            return candidates.Take(cap).Select(c => c.ImageUrl).ToList();
        }

        // Multimodal: each candidate sends both its descriptive text AND its image URL,
        // so Cohere's cross-encoder scores against the actual visual content (not just
        // a text proxy). For past-post candidates the URL is the S3-mirrored variant
        // (Cohere can fetch S3 just fine); for fresh-topic the URL is whatever Brave
        // returned.
        var docs = candidates
            .Select(c => new RerankDocument(Text: c.DescriptiveText, ImageUrl: c.ImageUrl))
            .ToList();
        IReadOnlyList<RerankResult> scored;
        try
        {
            scored = await _rerankClient.RerankAsync(query, docs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DraftPost {Id}: rerank threw; falling back to candidate order", taskId);
            return candidates.Take(cap).Select(c => c.ImageUrl).ToList();
        }

        if (scored.Count == 0)
        {
            _logger.LogWarning(
                "DraftPost {Id}: rerank returned 0 results for {DocCount} docs; falling back to candidate order",
                taskId, docs.Count);
            return candidates.Take(cap).Select(c => c.ImageUrl).ToList();
        }

        // Apply threshold + cap. Log the full picture so it's easy to tune the threshold.
        var ordered = scored.OrderByDescending(r => r.Score).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            if (r.Index < 0 || r.Index >= candidates.Count) continue;
            var c = candidates[r.Index];
            _logger.LogInformation(
                "DraftPost {Id}: rerank rank {Rank}/{Total} score={Score:F3} src={Source} url={Url} doc=\"{Doc}\"",
                taskId, i + 1, ordered.Count, r.Score, c.Source,
                c.ImageUrl[..Math.Min(c.ImageUrl.Length, 100)],
                c.DescriptiveText[..Math.Min(c.DescriptiveText.Length, 120)]);
        }

        var kept = ordered
            .Where(r => r.Score >= RerankRelevanceThreshold && r.Index >= 0 && r.Index < candidates.Count)
            .Take(cap)
            .Select(r => candidates[r.Index].ImageUrl)
            .ToList();

        var dropped = ordered.Count - kept.Count;
        _logger.LogInformation(
            "DraftPost {Id}: rerank kept {Kept}/{Total} (threshold={Threshold:F2}, cap={Cap}, dropped {Dropped})",
            taskId, kept.Count, ordered.Count, RerankRelevanceThreshold, cap, dropped);

        return kept;
    }

    /// <summary>
    /// Pulls the design rules for the requested style from the knowledge base
    /// (knowledge:image-design-{style}:*), bootstrapped at rag-microservice startup
    /// from <c>service/knowledge/image_design_*.md</c>.
    ///
    /// Returns the raw RAG context block (chunks + entities). Empty string on failure
    /// — a missing knowledge fetch must NOT drop the pipeline; the per-style addendum
    /// in the brief system prompt already encodes the most important rules.
    /// </summary>
    private async Task<string> FetchStyleKnowledgeAsync(string style, string language, CancellationToken cancellationToken)
    {
        try
        {
            var prefix = $"knowledge:image-design-{style}:";
            var query = LocalizedStyleDesignLiteral(language, style);
            var resp = await _ragClient.QueryAsync(
                new RagQueryRequest(
                    Query: query,
                    DocumentIdPrefix: prefix,
                    Mode: "naive",
                    TopK: 8,
                    OnlyNeedContext: true),
                cancellationToken);
            return resp.Answer ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch style knowledge for style={Style} — proceeding with brief-prompt addendum only",
                style);
            return string.Empty;
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + $"…[truncated {value.Length - max} more chars]";
    }

    private static string BuildImagePrompt(string userPrompt)
        => $"Generate an image for a social media post on this topic: {userPrompt}.\n\n" +
           "Match the visual style of the attached reference images: same palette, same lighting, " +
           "similar composition. Keep it brand-consistent.";

    /// <summary>
    /// Step 3.5 — calls gpt-4o-mini with the post caption + all RAG context (text refs,
    /// video segment captions/transcripts, knowledge guidance) to produce a focused
    /// JSON brief that the image-gen model will consume.
    ///
    /// Falls back to a generic brief if the LLM call fails or produces malformed JSON
    /// — we never want a single failing brief to drop the whole draft pipeline.
    /// </summary>
    private async Task<ImageBrief> BuildImageBriefAsync(
        string userPrompt,
        string caption,
        AccountRecommendationsAnswer rag,
        IReadOnlyList<string> topImageUrls,
        string style,
        string styleKnowledge,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"User's draft topic: {userPrompt}");
        sb.AppendLine($"Requested STYLE: {style}");
        sb.AppendLine();
        sb.AppendLine("Caption that will accompany the image:");
        sb.AppendLine($"\"\"\"{caption}\"\"\"");
        sb.AppendLine();

        // For `marketing` style, the brief instructs the image-gen model to render
        // the brand website / email / phone on the image. Pass the verbatim profile
        // so the brief quotes exact strings rather than fabricating canonical-looking
        // contact info. (We observed `meai.website` → `meai.com` hallucination.)
        if (!string.IsNullOrWhiteSpace(rag.PageProfileText))
        {
            sb.AppendLine("=== Page profile (verbatim — single source of truth for any text rendered on the image) ===");
            sb.AppendLine("If your brief asks the image-gen model to render the brand website / email / phone, " +
                          "QUOTE THESE STRINGS EXACTLY in the prompt. Do NOT paraphrase or invent variants. " +
                          "Omit any field not present here.");
            sb.AppendLine(rag.PageProfileText);
            sb.AppendLine("=== End of page profile ===");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(styleKnowledge))
        {
            sb.AppendLine($"=== Image-design rules for STYLE = {style} (from knowledge base — AUTHORITATIVE) ===");
            sb.AppendLine(styleKnowledge.Length > 3000 ? styleKnowledge[..3000] + "…" : styleKnowledge);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(rag.Answer))
        {
            sb.AppendLine("RAG recommendation summary (formulas, hooks, design heuristics already applied):");
            sb.AppendLine(rag.Answer.Length > 1500 ? rag.Answer[..1500] + "…" : rag.Answer);
            sb.AppendLine();
        }

        // Include past post captions + visual-style cues (so the brief LLM can
        // recognize the brand's voice/look beyond the raw image refs).
        var sampleRefs = rag.References.Take(6).ToList();
        if (sampleRefs.Count > 0)
        {
            sb.AppendLine("Recent past posts from this account (for style anchoring):");
            for (var i = 0; i < sampleRefs.Count; i++)
            {
                var r = sampleRefs[i];
                var captionSnippet = (r.Caption ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
                if (captionSnippet.Length > 200) captionSnippet = captionSnippet[..200] + "…";
                sb.AppendLine($"[{i + 1}] postId={r.PostId} caption=\"{captionSnippet}\"");

                // Video segment cues — the image-gen model can never see video, but
                // its motion-aware caption + transcript tell us what visual world the
                // user inhabits ("upbeat product unboxings", "kitchen close-ups", etc.)
                if (!string.IsNullOrWhiteSpace(r.VideoSegmentTime))
                {
                    var transcript = (r.VideoTranscript ?? string.Empty).Replace('\n', ' ');
                    if (transcript.Length > 200) transcript = transcript[..200] + "…";
                    sb.AppendLine($"     videoSegment time={r.VideoSegmentTime} transcript=\"{transcript}\"");
                }
            }
            sb.AppendLine();
        }

        if (topImageUrls.Count > 0)
        {
            sb.AppendLine($"The next {topImageUrls.Count} image(s) attached are the actual reference past-post images. " +
                          "Look at them carefully to lock in the brand's palette, lighting, mood, and composition.");
        }

        try
        {
            var briefResult = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: ImageBriefSystemPromptFor(style),
                    UserText: sb.ToString(),
                    ReferenceImageUrls: topImageUrls),
                cancellationToken);

            var json = StripJsonFence(briefResult.Answer ?? string.Empty);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var prompt = root.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
                ? (p.GetString() ?? string.Empty).Trim() : string.Empty;
            var styleNotes = root.TryGetProperty("style_notes", out var s) && s.ValueKind == JsonValueKind.String
                ? (s.GetString() ?? string.Empty).Trim() : string.Empty;
            var aspectRatio = root.TryGetProperty("aspect_ratio", out var a) && a.ValueKind == JsonValueKind.String
                ? (a.GetString() ?? "1:1").Trim() : "1:1";

            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.LogWarning("Image brief LLM returned empty prompt — falling back to generic");
                return BuildFallbackBrief(userPrompt);
            }
            return new ImageBrief(prompt, styleNotes, aspectRatio);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image brief LLM call or JSON parse failed — falling back to generic");
            return BuildFallbackBrief(userPrompt);
        }
    }

    private static ImageBrief BuildFallbackBrief(string userPrompt) =>
        new(BuildImagePrompt(userPrompt), string.Empty, "1:1");

    /// <summary>
    /// Strips ``` and ```json fences if the LLM wrapped its JSON in markdown despite
    /// being told not to. Robust to leading/trailing whitespace.
    /// </summary>
    private static string StripJsonFence(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            t = t["```json".Length..].TrimStart();
        else if (t.StartsWith("```"))
            t = t["```".Length..].TrimStart();
        if (t.EndsWith("```"))
            t = t[..^3].TrimEnd();
        return t;
    }

    private static string SerializeReferences(IReadOnlyList<RecommendationReference> references)
    {
        try
        {
            return JsonSerializer.Serialize(references, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        }
        catch
        {
            return "[]";
        }
    }

    /// <summary>
    /// Localized literal for the style-design knowledge query (R7). Same pattern as
    /// the profile / platform-formulas literals in QueryAccountRecommendationsQueryHandler.
    /// Falls back to English when there's no template entry.
    /// </summary>
    private static string LocalizedStyleDesignLiteral(string language, string style)
    {
        return language switch
        {
            "vi" => $"quy tắc thiết kế hình ảnh cho phong cách {style} trên mạng xã hội",
            "ja" => $"ソーシャルメディア投稿の{style}スタイル画像デザインルール",
            "ko" => $"소셜 미디어 게시물의 {style} 스타일 이미지 디자인 규칙",
            "th" => $"กฎการออกแบบภาพสำหรับสไตล์ {style} ในโซเชียลมีเดีย",
            "zh" => $"社交媒体帖子的 {style} 风格图像设计规则",
            "es" => $"reglas de diseño de imagen para estilo {style} en redes sociales",
            "pt" => $"regras de design de imagem para estilo {style} em redes sociais",
            "fr" => $"règles de conception d'image pour style {style} sur les réseaux sociaux",
            "de" => $"Bilddesign-Regeln für {style}-Stil in sozialen Medien",
            "id" => $"aturan desain gambar untuk gaya {style} di media sosial",
            _ => $"image design rules for {style} style social media post",
        };
    }
}
