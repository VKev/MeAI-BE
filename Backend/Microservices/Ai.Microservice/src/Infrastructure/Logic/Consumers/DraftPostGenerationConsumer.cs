using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.Abstractions.Billing;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
using Application.Abstractions.Search;
using Application.Billing;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
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
///   4. Media generation (image or Veo 3.1 Fast video, same references for visual style)
///   5. Upload generated media to S3 via User microservice
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
        "heuristics. These are optional heuristics, not rules; use them only when they fit " +
        "the account, prompt, topic, and platform, " +
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
        "CAPTION LENGTH LIMITS: The final caption must fit the MeAI publish limit for the " +
        "target platform, including hashtags, emojis, URLs, email, phone, and line breaks. " +
        "Facebook: 2,200 characters. Instagram: 2,200 characters. TikTok: 2,200 characters. " +
        "Threads: 500 characters. If the target platform is Threads, be concise and do not " +
        "write a caption that would need publish-time truncation.\n\n" +
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

    private const string ReferenceImageSimilarityGuard =
        "IMPORTANT: Reference images are for reference only; do not make the generated image " +
        "too similar to any reference image. Use them for palette, lighting, mood, and brand " +
        "cues, then create a new composition. ";

    private const string ReferenceImageSelectorSystemPrompt =
        "You are selecting visual reference images for an AI social-media image or video generation request. " +
        "You receive candidate images as attachments in the exact same order as the numbered candidate list. " +
        "Each candidate also has search/context text: source type, title, source page, originating search query, and description. " +
        "Choose a small balanced set that best helps the generator create the requested post.\n\n" +
        "Selection rules:\n" +
        "- Judge web-search candidates by BOTH the image pixels and the web-search context text. The title, source page, search query, and description tell you what the image is supposed to represent; do not choose by visual prettiness alone.\n" +
        "- Cover distinct visual subjects/entities explicitly requested by the user. If the prompt needs both a product/technology subject and a celebrity, artist, event, MV, location, or campaign reference, select at least one strong image for each when available.\n" +
        "- Prefer exact subject/entity matches over generic images. For example, an exact Son Tung M-TP / Come My Way MV image is more useful than another generic camera image when the prompt mentions that MV.\n" +
        "- Avoid near-duplicates. Do not fill the set with multiple similar product shots unless the request is only about that one product.\n" +
        "- Fresh web images are usually better for external/current subjects. Past-post/RAG images are usually better for the account's brand style. Use both when useful.\n" +
        "- Source slot counts are guidance, not hard rules; semantic coverage wins.\n\n" +
        "Return strict JSON only, no markdown, with this shape: " +
        "{\"selected\":[{\"candidate_number\":1,\"coverage\":\"what visual need this covers\",\"reason\":\"why this image is useful\"}]}";

    /// <summary>
    /// Image-gen system prompt for <c>creative</c> — pure visual, NO text rendering.
    /// </summary>
    private const string ImageSystemPromptCreative =
        "You are an image-generation assistant for social media. Produce ONE editorial-style " +
        "image that fits the user's topic AND matches the visual style of the reference images " +
        "attached (color palette, lighting, composition, mood, subject framing). " +
        ReferenceImageSimilarityGuard +
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
        ReferenceImageSimilarityGuard +
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
        ReferenceImageSimilarityGuard +
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

    private static string BuildDraftImagePrompt(ImageBrief brief, DraftImageTarget target)
    {
        return
            $"{brief.Prompt}\n\n" +
            $"Aspect ratio: {brief.AspectRatio}. " +
            BuildDraftImageVariationDirective(target) + " " +
            "Render at high resolution. Output an image, no text response.";
    }

    private static string BuildDraftVideoPrompt(ImageBrief brief)
    {
        return
            $"{brief.Prompt}\n\n" +
            $"Visual direction: {brief.StyleNotes}\n\n" +
            "Create one publish-ready 8-second social media video at 720p in 16:9. " +
            "Use the supplied images as visual references for palette, lighting, mood, subject, and brand direction. " +
            "Add intentional camera motion and natural scene movement. Keep the sequence visually coherent and suitable for a social post.";
    }

    private static string BuildDraftImageSystemPrompt(string baseSystemPrompt, DraftImageTarget target)
    {
        if (target.Total <= 1)
        {
            return baseSystemPrompt;
        }

        return baseSystemPrompt + "\n\n" +
            "This is a multi-image draft post. Generate ONLY media item " +
            $"{target.Ordinal} of {target.Total}. Each item in the set must be visibly distinct: " +
            "different composition, framing, background, subject pose or product angle, and any text placement. " +
            "Keep the same campaign idea and brand direction, but do not create duplicate-looking images.";
    }

    private static string BuildDraftImageVariationDirective(DraftImageTarget target)
    {
        if (target.Total <= 1)
        {
            return "Create one complete, publish-ready image for the post.";
        }

        var focus = ((target.Ordinal - 1) % 4) switch
        {
            0 => "hero overview with the main product or subject clearly established",
            1 => "lifestyle or real-use scene that shows the benefit in context",
            2 => "detail or feature-focused close-up with a different crop and background",
            _ => "action, comparison, or audience-focused scene with a distinct visual angle",
        };

        return
            $"Create media item {target.Ordinal} of {target.Total} for the same post. " +
            $"Variation focus: {focus}. Do not copy the layout, crop, background, or rendered text arrangement from the other images in this set.";
    }

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
        "RAG knowledge base (image-design-{style}) — optional style heuristics that should be " +
        "used only when relevant to this account, prompt, and target platform, " +
        "(f) the target platform, (g) the requested STYLE.\n\n" +
        "IMPORTANT: Reference images are for reference only; do not make the generated image too " +
        "similar to any reference image. Use them for palette, lighting, mood, and brand cues, " +
        "then create a new composition.\n\n" +
        "IMPORTANT: Treat RAG formulas, platform rules, and style knowledge as suggestions. " +
        "Evaluate fit first. If a formula or rule would make the recommendation generic, off-brand, " +
        "too salesy, or wrong for the prompt/account, ignore it and build the brief from the caption, " +
        "brand profile, and selected references instead.\n\n" +
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
            "IMPORTANT NOVELTY RULE: use the past posts as an exclusion list for the specific subject and angle, " +
            "not as a list of topics to repeat. The new topic may stay inside the same broad content pillar or " +
            "product/service category, but it must NOT be the same specific item, model, offer, event, claim, " +
            "audience problem, use case, or hook that the account already published recently. If the best current " +
            "trend overlaps with a prior post, choose a meaningfully distinct adjacent angle: a new subtopic, " +
            "new feature/benefit, comparison, seasonal/current development, audience segment, objection, or " +
            "practical use case. Balance freshness with brand fit; do not force novelty by going off-niche. " +
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

    private sealed record DraftImageTarget(int Ordinal, int Total);

    private sealed record GeneratedDraftImage(int Ordinal, int Total, ImageGenerationResult Result);

    private sealed record GeneratedDraftMedia(
        int Ordinal,
        int Total,
        string Url,
        string MimeType,
        bool IsDataUrl = false,
        int? PromptTokens = null,
        int? CompletionTokens = null,
        decimal? CostUsd = null,
        string? ProviderTaskId = null,
        string? Resolution = null);

    private sealed record UploadedDraftMedia(
        int Ordinal,
        int Total,
        Guid ResourceId,
        string PresignedUrl,
        string? ContentType,
        string? ResourceType,
        string? OriginKind,
        string? OriginSourceUrl,
        Guid? OriginChatSessionId,
        Guid? OriginChatId);

    private sealed record FreshTopicImageSearchOutcome(
        IReadOnlyList<Application.Abstractions.Search.ImageSearchHit> Hits,
        Exception? Error = null,
        IReadOnlyList<string>? Queries = null,
        IReadOnlyList<FreshTopicImageQueryOutcome>? QueryOutcomes = null);

    private sealed record FreshTopicImageQueryOutcome(
        string Query,
        int HitCount,
        string? ErrorCode = null,
        string? ErrorMessage = null);

    private sealed record FreshTopicImageMirrorOutcome(
        IReadOnlyList<Application.Abstractions.Search.ImageSearchHit> Hits,
        IReadOnlyList<object> Mirrors,
        IReadOnlyList<object> Failures);

    private sealed record ReferenceImageSelectionOutcome(
        List<SelectedReferenceImage> SelectedImages,
        Exception? Error = null,
        string Strategy = "rerank")
    {
        public List<string> SelectedImageUrls => SelectedImages.Select(image => image.ImageUrl).ToList();
    }

    private sealed record SelectedReferenceImage(
        string ImageUrl,
        string Source,
        string DescriptiveText,
        string? Title,
        string? SourcePageUrl,
        string? SearchQuery,
        double? Score,
        int? Rank,
        string? Coverage = null,
        string? SelectionReason = null);

    private sealed record ReferenceImageSlotAllocation(int Web, int Rag)
    {
        public int Total => Web + Rag;
    }

    private sealed record ScoredImageRefCandidate(ImageRefCandidate Candidate, double? Score, int Rank);

    private sealed record IndexedImageRefCandidate(int CandidateNumber, int OriginalIndex, ImageRefCandidate Candidate);

    private sealed record StyleKnowledgeOutcome(
        string Knowledge,
        Exception? Error = null);

    private sealed record ImageBriefOutcome(
        ImageBrief Brief,
        Exception? Error = null);

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
    private const int MaxVideoReferenceImages = 3;
    private const string RecommendationVideoModel = "veo-3-1";
    private const string RecommendationVideoVariant = "fast";
    private const string RecommendationVideoResolution = "720p";
    private const int RecommendationVideoDurationSeconds = 8;
    private const string RecommendationVideoAspectRatio = "16:9";
    private static readonly TimeSpan RecommendationVideoPollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecommendationVideoTimeout = TimeSpan.FromMinutes(10);

    private readonly IMediator _mediator;
    private readonly IDraftPostTaskRepository _taskRepository;
    private readonly IPostRepository _postRepository;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly IImageGenerationClient _imageGenClient;
    private readonly IVeoVideoService _veoVideoService;
    private readonly IUserResourceService _userResourceService;
    private readonly IRagClient _ragClient;
    private readonly IImageSearchClient _imageSearchClient;
    private readonly IRerankClient _rerankClient;
    private readonly Application.Recommendations.Services.IQueryRewriter _queryRewriter;
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;
    private readonly IBillingClient _billingClient;
    private readonly ILogger<DraftPostGenerationConsumer> _logger;

    public DraftPostGenerationConsumer(
        IMediator mediator,
        IDraftPostTaskRepository taskRepository,
        IPostRepository postRepository,
        IMultimodalLlmClient multimodalLlm,
        IImageGenerationClient imageGenClient,
        IVeoVideoService veoVideoService,
        IUserResourceService userResourceService,
        IRagClient ragClient,
        IImageSearchClient imageSearchClient,
        IRerankClient rerankClient,
        Application.Recommendations.Services.IQueryRewriter queryRewriter,
        IAiSpendRecordRepository aiSpendRecordRepository,
        IBillingClient billingClient,
        ILogger<DraftPostGenerationConsumer> logger)
    {
        _mediator = mediator;
        _taskRepository = taskRepository;
        _postRepository = postRepository;
        _multimodalLlm = multimodalLlm;
        _imageGenClient = imageGenClient;
        _veoVideoService = veoVideoService;
        _userResourceService = userResourceService;
        _ragClient = ragClient;
        _imageSearchClient = imageSearchClient;
        _rerankClient = rerankClient;
        _queryRewriter = queryRewriter;
        _aiSpendRecordRepository = aiSpendRecordRepository;
        _billingClient = billingClient;
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

    private Task PublishErrorThinkingAsync(
        ConsumeContext<GenerateDraftPostStarted> context,
        DraftPostTask task,
        string action,
        string title,
        string message,
        Exception error,
        object? details,
        CancellationToken cancellationToken,
        string phaseStatus = "failed")
    {
        return PublishThinkingAsync(
            context,
            task,
            action,
            title,
            message,
            new
            {
                errorCode = error.GetType().Name,
                errorMessage = error.Message,
                exception = Truncate(error.ToString(), 6000),
                details,
            },
            cancellationToken,
            phaseStatus: phaseStatus);
    }

    private static bool LooksLikeProviderCreditFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("HTTP 402", StringComparison.OrdinalIgnoreCase)
               || error.Contains("Insufficient credits", StringComparison.OrdinalIgnoreCase);
    }

    private Task PublishAccountPostsReadProgressAsync(
        ConsumeContext<GenerateDraftPostStarted> context,
        DraftPostTask task,
        IndexSocialAccountIngestProgress progress,
        CancellationToken cancellationToken)
    {
        var total = Math.Max(progress.TotalDocuments, 0);
        var completed = total > 0
            ? Math.Clamp(progress.CompletedDocuments, 0, total)
            : Math.Max(progress.CompletedDocuments, 0);
        var batchStart = total > 0 ? Math.Clamp(progress.CurrentBatchStart, 1, total) : 0;
        var batchEnd = total > 0 ? Math.Clamp(progress.CurrentBatchEnd, batchStart, total) : 0;
        var label = total > 0
            ? batchEnd > batchStart ? $"{batchStart}-{batchEnd}/{total}" : $"{completed}/{total}"
            : "0/0";

        return PublishThinkingAsync(
            context,
            task,
            "account_posts_reading_started",
            "AI is reading account posts",
            total > 0
                ? $"AI is indexing account knowledge ({label})."
                : "AI checked account knowledge; nothing new needs indexing.",
            new
            {
                socialMediaId = progress.SocialMediaId,
                platform = progress.Platform,
                documentIdPrefix = progress.DocumentIdPrefix,
                completedDocuments = completed,
                totalDocuments = total,
                currentBatchStart = batchStart,
                currentBatchEnd = batchEnd,
                progressLabel = label,
                ingestedDocuments = progress.IngestedDocuments,
                unchangedDocuments = progress.UnchangedDocuments,
                failedDocuments = progress.FailedDocuments,
            },
            cancellationToken);
    }

    public async Task Consume(ConsumeContext<GenerateDraftPostStarted> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;
        // Style is normalized at command-handler time, but we re-normalize here so
        // older queued messages (with no Style field) and any direct re-publishes
        // both default cleanly to "branded".
        var style = DraftPostStyles.NormalizeOrDefault(msg.Style);
        var mediaType = DraftPostMediaTypes.NormalizeOrDefault(msg.MediaType);
        var maxReferenceImages = string.Equals(mediaType, DraftPostMediaTypes.Video, StringComparison.Ordinal)
            ? Math.Clamp(msg.MaxReferenceImages, 1, MaxVideoReferenceImages)
            : Math.Max(msg.MaxReferenceImages, 1);
        var referenceImageSlots = AllocateReferenceImageSlots(mediaType, maxReferenceImages);
        var isAutoTopic = msg.IsAutoTopic;

        _logger.LogInformation(
            "DraftPost: starting CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId} Style={Style} MediaType={MediaType} AutoTopic={Auto}",
            msg.CorrelationId, msg.UserId, msg.SocialMediaId, style, mediaType, isAutoTopic);

        var task = await _taskRepository.GetByCorrelationIdForUpdateAsync(msg.CorrelationId, ct);
        if (task is null)
        {
            _logger.LogWarning("DraftPost: task not found for CorrelationId={CorrelationId}", msg.CorrelationId);
            return;
        }

        // Tracks whether the empty Post (pre-created by StartDraftPostGenerationCommand)
        // has been finalized with caption + media. If we fail BEFORE this flips true,
        // the catch path soft-deletes the empty Post so the FE doesn't see a permanent
        // blank placeholder. After it's true, the Post has real content and we keep it.
        bool postFinalized = false;

        try
        {
            task.Status = DraftPostTaskStatuses.Processing;
            task.MediaType = mediaType;
            task.ImageCount = string.Equals(mediaType, DraftPostMediaTypes.Video, StringComparison.Ordinal)
                ? 1
                : task.ImageCount;
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
                    mediaType,
                    userPrompt = msg.UserPrompt,
                    isAutoTopic,
                    topK = msg.TopK,
                    maxReferenceImages,
                    maxRagPosts = msg.MaxRagPosts,
                },
                ct);

            // Step 0 — block until rag-microservice's lazy knowledge bootstrap is done.
            // After the first cold call this returns instantly. Doing this BEFORE Step 1
            // means every downstream RAG/LLM/image-gen call runs against a fully-built
            // knowledge index, never a half-built one.
            _logger.LogInformation("DraftPost {Id}: waiting for RAG to be ready...", task.Id);
            await _ragClient.WaitForRagReadyAsync(ct);
            _logger.LogInformation("DraftPost {Id}: RAG ready", task.Id);

            // Step 1 — auto-index. Existing skip-if-unchanged logic ensures only new/changed
            // posts hit RAG; unchanged ones are no-ops.
            var indexMaxPosts = msg.MaxRagPosts > 0 ? msg.MaxRagPosts : 30;
            _logger.LogDebug("DraftPost {Id}: indexing posts (max={Max})...", task.Id, indexMaxPosts);
            await PublishThinkingAsync(
                context,
                task,
                "account_posts_reading_started",
                "AI is reading account posts",
                "AI is fetching recent account posts before updating RAG knowledge.",
                new
                {
                    socialMediaId = msg.SocialMediaId,
                    maxPosts = indexMaxPosts,
                    purpose = "Read recent account content before RAG indexing.",
                },
                ct);
            var indexResult = await _mediator.Send(
                new IndexSocialAccountPostsCommand(
                    msg.UserId,
                    msg.SocialMediaId,
                    indexMaxPosts,
                    OnIngestFailures: null,
                    OnReadBatch: null,
                    StopOnProviderCreditFailure: true,
                    OnIngestProgress: (progress, cancellationToken) =>
                        PublishAccountPostsReadProgressAsync(context, task, progress, cancellationToken),
                    BackfillMissingMediaDocuments: false),
                ct);
            if (indexResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Indexing failed: {indexResult.Error.Code} {indexResult.Error.Description}");
            }
            var failedIngestDocuments = indexResult.Value.FailedIngestDocuments ?? Array.Empty<IndexSocialAccountIngestFailure>();
            if (failedIngestDocuments.Any(item => LooksLikeProviderCreditFailure(item.Error)))
            {
                throw new InvalidOperationException(
                    "OpenRouter has insufficient credits for RAG media analysis. Add credits in OpenRouter, then try generating this recommendation again.");
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
                    nonBlockingFailedDocuments = failedIngestDocuments.Count,
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
                    KeyTerms: Array.Empty<string>(),
                    VisualQueries: new[] { recommendationQuery });
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
                    visualQueries = rewrite.VisualQueries,
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
                        visualQueries = rewrite.VisualQueries,
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
                    visualQueries = rewrite.VisualQueries,
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
            if (rag.RetrievalErrors is { Count: > 0 } retrievalErrors)
            {
                await PublishThinkingAsync(
                    context,
                    task,
                    "rag_retrieval_partial_failure",
                    "Some knowledge retrieval failed",
                    "AI will continue with the RAG context that was retrieved successfully.",
                    new
                    {
                        socialMediaId = msg.SocialMediaId,
                        query = recommendationQuery,
                        retrievalErrors,
                    },
                    ct,
                    phaseStatus: "warning");
            }
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
                        DescriptiveText: BuildPastPostCandidateText(r),
                        Title: string.IsNullOrWhiteSpace(r.PostId) ? "Past post image" : $"Past post {r.PostId}"));
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
                            DescriptiveText: BuildVideoFrameCandidateText(r, frameUrl),
                            Title: string.IsNullOrWhiteSpace(r.PostId)
                                ? "Past post video frame"
                                : $"Past post {r.PostId} video frame"));
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
                    query = recommendationQuery,
                    primaryQuery = rewrite.PrimaryQuery,
                    altQueries = rewrite.AltQueries,
                    visualQuery = rewrite.VisualQuery,
                    visualQueries = rewrite.VisualQueries,
                    keyTerms = rewrite.KeyTerms,
                    documentIdPrefix = rag.DocumentIdPrefix,
                    answer = rag.Answer,
                    pageProfileText = rag.PageProfileText,
                    references = rag.References,
                    webSources = rag.WebSources,
                    retrievalErrors = rag.RetrievalErrors,
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
            var captionRefImageUrls = topImageUrls.Take(maxReferenceImages).ToList();
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
            var refImageQueries = BuildFreshTopicImageQueries(
                refImageQuery,
                rewrite,
                recommendationQuery,
                msg.UserPrompt);
            var freshTopicCandidateLimit = CalculateFreshTopicCandidateLimit(referenceImageSlots.Web);
            await PublishThinkingAsync(
                context,
                task,
                "fresh_image_search_started",
                "AI is finding visual references",
                "AI is searching for fresh real-world image references for the chosen topic.",
                new
                {
                    query = refImageQuery,
                    queries = refImageQueries,
                    visualQuery = rewrite.VisualQuery,
                    visualQueries = rewrite.VisualQueries,
                    webReferenceSlots = referenceImageSlots.Web,
                    ragReferenceSlots = referenceImageSlots.Rag,
                    candidateLimit = freshTopicCandidateLimit,
                    source = "brave-image-search",
                },
                ct);
            var freshRefImageOutcome = await FetchFreshTopicImageHitsAsync(
                refImageQueries,
                freshTopicCandidateLimit,
                ct);
            if (freshRefImageOutcome.Error is not null)
            {
                await PublishErrorThinkingAsync(
                    context,
                    task,
                    "fresh_image_search_failed",
                    "Fresh image search failed",
                    "AI could not search fresh image references, so it will continue with account references.",
                    freshRefImageOutcome.Error,
                    new
                    {
                        query = refImageQuery,
                        queries = refImageQueries,
                        queryOutcomes = freshRefImageOutcome.QueryOutcomes,
                        source = "brave-image-search",
                    },
                    ct,
                    phaseStatus: "warning");
            }
            var freshRefImageMirrorOutcome = await MirrorFreshTopicImageHitsAsync(
                msg.UserId,
                msg.WorkspaceId,
                freshRefImageOutcome.Hits,
                ct);
            var freshRefImageHits = freshRefImageMirrorOutcome.Hits;
            _logger.LogInformation(
                "DraftPost {Id}: fresh-ref-image search queries=[{Queries}] -> {Count} hit(s)",
                task.Id, string.Join(" | ", refImageQueries), freshRefImageHits.Count);
            var freshTopicCandidates = freshRefImageHits
                .Select(h => new ImageRefCandidate(
                    ImageUrl: h.ImageUrl,
                    Source: "fresh-topic",
                    DescriptiveText: BuildFreshTopicCandidateText(h, h.Query ?? refImageQuery),
                    Title: h.Title,
                    SourcePageUrl: h.SourcePageUrl,
                    SearchQuery: h.Query ?? refImageQuery))
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
                    queries = refImageQueries,
                    queryOutcomes = freshRefImageOutcome.QueryOutcomes,
                    webReferenceSlots = referenceImageSlots.Web,
                    ragReferenceSlots = referenceImageSlots.Rag,
                    hits = freshRefImageHits,
                    mirroredImages = freshRefImageMirrorOutcome.Mirrors,
                    mirrorFailures = freshRefImageMirrorOutcome.Failures,
                    candidateUrls = freshTopicCandidates.Select(candidate => candidate.ImageUrl).ToList(),
                    candidates = freshTopicCandidates.Select(candidate => new
                    {
                        candidate.ImageUrl,
                        candidate.Source,
                        candidate.DescriptiveText,
                        candidate.Title,
                        candidate.SourcePageUrl,
                        candidate.SearchQuery,
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
                "AI is selecting a balanced set of past-post and fresh-topic images for the draft.",
                new
                {
                    topic = refImageQuery,
                    caption,
                    visualQuery = rewrite.VisualQuery,
                    visualQueries = rewrite.VisualQueries,
                    keyTerms = rewrite.KeyTerms,
                    cap = maxReferenceImages,
                    webReferenceSlots = referenceImageSlots.Web,
                    ragReferenceSlots = referenceImageSlots.Rag,
                    candidateCount = rerankCandidates.Count,
                    candidates = rerankCandidates.Select(candidate => new
                    {
                        candidate.ImageUrl,
                        candidate.Source,
                        candidate.DescriptiveText,
                        candidate.Title,
                        candidate.SourcePageUrl,
                        candidate.SearchQuery,
                    }).ToList(),
                },
                ct);
            var referenceSelection = await SelectReferenceImagesAsync(
                taskId: task.Id,
                candidates: rerankCandidates,
                topic: refImageQuery,
                caption: caption,
                visualQuery: rewrite.VisualQuery,                  // ← K4 enhancement
                keyTerms: rewrite.KeyTerms,                        // ← K4 enhancement
                cap: maxReferenceImages,
                allocation: referenceImageSlots,
                cancellationToken: ct);
            if (referenceSelection.Error is not null)
            {
                await PublishErrorThinkingAsync(
                    context,
                    task,
                    "reference_rerank_failed",
                    "Reference image ranking failed",
                    "AI could not rank reference images, so it will continue with the original candidate order.",
                    referenceSelection.Error,
                    new
                    {
                        topic = refImageQuery,
                        caption,
                        visualQuery = rewrite.VisualQuery,
                        visualQueries = rewrite.VisualQueries,
                        keyTerms = rewrite.KeyTerms,
                        cap = maxReferenceImages,
                        webReferenceSlots = referenceImageSlots.Web,
                        ragReferenceSlots = referenceImageSlots.Rag,
                        candidateCount = rerankCandidates.Count,
                    },
                    ct,
                    phaseStatus: "warning");
            }
            var imageBriefRefImageUrls = referenceSelection.SelectedImageUrls;
            await PublishThinkingAsync(
                context,
                task,
                "reference_rerank_completed",
                "AI chose reference images",
                "AI selected the images that best cover the draft topic, caption, and visual references.",
                new
                {
                    selectionMode = referenceSelection.Strategy,
                    selectedReferenceImageUrls = imageBriefRefImageUrls,
                    selectedReferences = referenceSelection.SelectedImages,
                    hits = referenceSelection.SelectedImages,
                    results = referenceSelection.SelectedImages,
                    selectedCount = imageBriefRefImageUrls.Count,
                    selectedWebCount = referenceSelection.SelectedImages.Count(image => IsFreshTopicCandidate(image.Source)),
                    selectedRagCount = referenceSelection.SelectedImages.Count(image => !IsFreshTopicCandidate(image.Source)),
                    webReferenceSlots = referenceImageSlots.Web,
                    ragReferenceSlots = referenceImageSlots.Rag,
                    candidateCount = rerankCandidates.Count,
                    cap = maxReferenceImages,
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
            var styleKnowledgeOutcome = await FetchStyleKnowledgeAsync(style, rewrite.Language, ct);
            if (styleKnowledgeOutcome.Error is not null)
            {
                await PublishErrorThinkingAsync(
                    context,
                    task,
                    "style_knowledge_failed",
                    "Style knowledge lookup failed",
                    $"AI could not read {style} image-design knowledge, so it will continue with built-in style guidance.",
                    styleKnowledgeOutcome.Error,
                    new
                    {
                        style,
                        language = rewrite.Language,
                        documentIdPrefix = styleKnowledgeDocumentIdPrefix,
                    },
                    ct,
                    phaseStatus: "warning");
            }
            var styleKnowledge = styleKnowledgeOutcome.Knowledge;
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
            var imageBriefOutcome = await BuildImageBriefAsync(
                userPrompt: topicForDownstream,
                caption: caption,
                rag: rag,
                topImageUrls: imageBriefRefImageUrls,
                style: style,
                styleKnowledge: styleKnowledge,
                cancellationToken: ct);
            if (imageBriefOutcome.Error is not null)
            {
                await PublishErrorThinkingAsync(
                    context,
                    task,
                    "image_brief_generation_failed",
                    "Image brief generation fell back",
                    "AI could not create a structured image brief, so it will continue with a fallback brief.",
                    imageBriefOutcome.Error,
                    new
                    {
                        style,
                        topic = topicForDownstream,
                        caption,
                        referenceImageUrls = imageBriefRefImageUrls,
                    },
                    ct,
                    phaseStatus: "warning");
            }
            var brief = imageBriefOutcome.Brief;
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

            // Step 4 — media generation. Image drafts use the multimodal image provider;
            // video drafts reuse the same RAG-grounded brief and selected references,
            // then submit one Veo 3.1 Fast clip.
            await PublishThinkingAsync(
                context,
                task,
                "image_brief_generation_completed",
                "AI planned the media",
                "AI finished the media-generation brief.",
                new
                {
                    mediaType,
                    prompt = brief.Prompt,
                    brief.AspectRatio,
                    brief.StyleNotes,
                    referenceImageUrls = imageBriefRefImageUrls,
                },
                ct,
                phaseStatus: "completed");
            List<GeneratedDraftMedia> generatedMedia;
            if (string.Equals(mediaType, DraftPostMediaTypes.Video, StringComparison.Ordinal))
            {
                task.ImageCount = 1;
                var videoReferenceImageUrls = imageBriefRefImageUrls.Take(MaxVideoReferenceImages).ToList();
                var videoPrompt = BuildDraftVideoPrompt(brief);
                await PublishThinkingAsync(
                    context,
                    task,
                    "video_generation_started",
                    "AI is generating the video",
                    "AI is generating an 8-second Veo 3.1 Fast video from the RAG-grounded brief and selected references.",
                    new
                    {
                        style,
                        prompt = videoPrompt,
                        model = RecommendationVideoModel,
                        variant = RecommendationVideoVariant,
                        resolution = RecommendationVideoResolution,
                        duration = RecommendationVideoDurationSeconds,
                        aspectRatio = RecommendationVideoAspectRatio,
                        generationType = videoReferenceImageUrls.Count > 0 ? "REFERENCE_2_VIDEO" : "TEXT_2_VIDEO",
                        referenceImageUrls = videoReferenceImageUrls,
                    },
                    ct);
                var generatedVideo = await GenerateRecommendationVideoAsync(videoPrompt, videoReferenceImageUrls, ct);
                generatedMedia = new List<GeneratedDraftMedia> { generatedVideo };
                await PublishThinkingAsync(
                    context,
                    task,
                    "video_generation_completed",
                    "AI generated the video",
                    "AI finished generating the Veo 3.1 Fast video.",
                    new
                    {
                        generatedVideo.ProviderTaskId,
                        generatedVideo.Resolution,
                        duration = RecommendationVideoDurationSeconds,
                        urlLength = generatedVideo.Url.Length,
                    },
                    ct,
                    phaseStatus: "completed");
            }
            else
            {
                var imageCount = Math.Clamp(msg.ImageCount > 0 ? msg.ImageCount : task.ImageCount > 0 ? task.ImageCount : 1, 1, 4);
                task.ImageCount = imageCount;
                var imageBaseSystem = ImageSystemPromptFor(style);
                var imageTargets = Enumerable.Range(1, imageCount)
                    .Select(ordinal => new DraftImageTarget(ordinal, imageCount))
                    .ToList();
                var baseImageSystemPrompt = string.IsNullOrWhiteSpace(brief.StyleNotes)
                    ? imageBaseSystem
                    : $"{imageBaseSystem}\n\nAdditional style constraints from the art-director brief: {brief.StyleNotes}";
                foreach (var imageTarget in imageTargets)
                {
                    var targetPrompt = BuildDraftImagePrompt(brief, imageTarget);
                    var targetSystemPrompt = BuildDraftImageSystemPrompt(baseImageSystemPrompt, imageTarget);
                    _logger.LogInformation(
                        "IMAGEGEN INPUT for DraftPost {Id} Style={Style} Media={Ordinal}/{Total} ({RefCount} ref images = {PastCount} past-post + {FreshCount} fresh-topic):\n  --- prompt ---\n{Prompt}\n  --- system ---\n{System}",
                        task.Id, style, imageTarget.Ordinal, imageTarget.Total,
                        imageBriefRefImageUrls.Count, topImageUrls.Count, freshRefImageUrls.Count,
                        Truncate(targetPrompt, 2500),
                        Truncate(targetSystemPrompt, 1500));
                    await PublishThinkingAsync(
                        context,
                        task,
                        $"image_generation_started_{imageTarget.Ordinal}",
                        imageTarget.Total == 1
                            ? "AI is generating the image"
                            : $"AI is generating draft media {imageTarget.Ordinal}/{imageTarget.Total}",
                        "AI is generating draft media from the brief and selected references.",
                        new
                        {
                            style,
                            prompt = targetPrompt,
                            systemPrompt = targetSystemPrompt,
                            mediaOrdinal = imageTarget.Ordinal,
                            mediaTotal = imageTarget.Total,
                            referenceImageUrls = imageBriefRefImageUrls,
                        },
                        ct);
                }

                var generatedImages = (await Task.WhenAll(imageTargets.Select(async imageTarget =>
                {
                    var targetPrompt = BuildDraftImagePrompt(brief, imageTarget);
                    var targetSystemPrompt = BuildDraftImageSystemPrompt(baseImageSystemPrompt, imageTarget);
                    var result = await _imageGenClient.GenerateImageAsync(
                        new ImageGenerationRequest(
                            Prompt: targetPrompt,
                            ReferenceImageUrls: imageBriefRefImageUrls,
                            SystemPrompt: targetSystemPrompt),
                        ct);
                    return new GeneratedDraftImage(imageTarget.Ordinal, imageTarget.Total, result);
                })))
                    .OrderBy(item => item.Ordinal)
                    .ToList();

                foreach (var generatedImage in generatedImages)
                {
                    _logger.LogInformation(
                        "IMAGEGEN OUTPUT for DraftPost {Id} Media={Ordinal}/{Total}: mime={MimeType}, urlLen={Len}, inlineData={InlineData}, promptTokens={Pt}, completionTokens={Ct}, costUsd={Cost}",
                        task.Id, generatedImage.Ordinal, generatedImage.Total, generatedImage.Result.MimeType,
                        generatedImage.Result.Url.Length, generatedImage.Result.IsDataUrl, generatedImage.Result.PromptTokens,
                        generatedImage.Result.CompletionTokens, generatedImage.Result.CostUsd);
                    await PublishThinkingAsync(
                        context,
                        task,
                        $"image_generation_completed_{generatedImage.Ordinal}",
                        generatedImage.Total == 1
                            ? "AI generated the image"
                            : $"AI generated draft media {generatedImage.Ordinal}/{generatedImage.Total}",
                        "AI finished generating draft media.",
                        new
                        {
                            generatedImage.Result.MimeType,
                            urlLength = generatedImage.Result.Url.Length,
                            generatedImage.Result.IsDataUrl,
                            generatedImage.Result.PromptTokens,
                            generatedImage.Result.CompletionTokens,
                            generatedImage.Result.CostUsd,
                            mediaOrdinal = generatedImage.Ordinal,
                            mediaTotal = generatedImage.Total,
                        },
                        ct,
                        phaseStatus: "completed");
                }
                generatedMedia = generatedImages
                    .Select(image => new GeneratedDraftMedia(
                        image.Ordinal,
                        image.Total,
                        image.Result.Url,
                        image.Result.MimeType,
                        image.Result.IsDataUrl,
                        image.Result.PromptTokens,
                        image.Result.CompletionTokens,
                        image.Result.CostUsd))
                    .ToList();
            }

            // Step 5 — upload generated media to S3. KIE results stay as provider URLs;
            // providers that only return inline bytes still flow through as `data:` URLs.
            _logger.LogDebug("DraftPost {Id}: uploading {Count} generated {MediaType} resource(s) to S3...", task.Id, generatedMedia.Count, mediaType);
            await PublishThinkingAsync(
                context,
                task,
                "resource_upload_started",
                generatedMedia.Count == 1 ? "AI is saving the media" : "AI is saving generated media",
                generatedMedia.Count == 1
                    ? "AI is uploading the generated media to workspace storage."
                    : "AI is uploading the generated media to workspace storage.",
                new
                {
                    workspaceId = msg.WorkspaceId,
                    resourceType = mediaType,
                    status = "generated",
                    mediaCount = generatedMedia.Count,
                    contentTypes = generatedMedia.Select(media => media.MimeType).ToList(),
                    originKind = ResourceOriginKinds.AiGenerated,
                },
                ct);
            var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                userId: msg.UserId,
                urls: generatedMedia.Select(media => media.Url).ToArray(),
                status: "generated",
                resourceType: mediaType,
                cancellationToken: ct,
                workspaceId: msg.WorkspaceId,
                provenance: new ResourceProvenanceMetadata(
                    OriginKind: ResourceOriginKinds.AiGenerated,
                    OriginChatSessionId: null,
                    OriginChatId: null));

            if (uploadResult.IsFailure || uploadResult.Value.Count < generatedMedia.Count)
            {
                throw new InvalidOperationException(
                    $"S3 upload failed: {uploadResult.Error?.Code} {uploadResult.Error?.Description}");
            }
            var uploadedMedia = uploadResult.Value
                .Take(generatedMedia.Count)
                .Select((uploaded, index) =>
                    new UploadedDraftMedia(
                        generatedMedia[index].Ordinal,
                        generatedMedia[index].Total,
                        uploaded.ResourceId,
                        uploaded.PresignedUrl,
                        uploaded.ContentType,
                        uploaded.ResourceType,
                        uploaded.OriginKind,
                        uploaded.OriginSourceUrl,
                        uploaded.OriginChatSessionId,
                        uploaded.OriginChatId))
                .ToList();
            await PublishThinkingAsync(
                context,
                task,
                "resource_upload_completed",
                uploadedMedia.Count == 1 ? "AI saved the media" : "AI saved generated media",
                uploadedMedia.Count == 1
                    ? "AI finished uploading the generated media."
                    : "AI finished uploading the generated media.",
                new
                {
                    mediaType,
                    resourceIds = uploadedMedia.Select(media => media.ResourceId).ToList(),
                    presignedUrls = uploadedMedia.Select(media => media.PresignedUrl).ToList(),
                    mediaTotal = uploadedMedia.Count,
                    items = uploadedMedia,
                },
                ct,
                phaseStatus: "completed");

            // Step 6 — populate the draft Post with the generated caption + media.
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
                "AI is saving the generated caption and media on the draft post.",
                new
                {
                    mediaType,
                    draftPostId = task.ResultPostId,
                    hasPrecreatedDraftPost = task.ResultPostId.HasValue,
                    resourceIds = uploadedMedia.Select(media => media.ResourceId).ToList(),
                    caption,
                },
                ct);
            var content = new PostContent
            {
                Content = caption,
                ResourceList = uploadedMedia.Select(media => media.ResourceId.ToString()).ToList(),
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
            var primaryUploaded = uploadedMedia[0];
            var resultResourceIds = uploadedMedia.Select(media => media.ResourceId).ToList();
            var resultPresignedUrls = uploadedMedia.Select(media => media.PresignedUrl).ToList();
            await PublishThinkingAsync(
                context,
                task,
                "draft_post_finalized",
                "AI finalized the draft",
                "AI saved the generated caption and media on the draft post.",
                new
                {
                    mediaType,
                    draftPostId = draftPost.Id,
                    resourceId = primaryUploaded.ResourceId,
                    presignedUrl = primaryUploaded.PresignedUrl,
                    resourceIds = resultResourceIds,
                    presignedUrls = resultPresignedUrls,
                    caption,
                },
                ct,
                phaseStatus: "completed");

            // Step 7 — mark task completed + notify
            task.Status = DraftPostTaskStatuses.Completed;
            task.ResultPostBuilderId = null;
            task.ResultPostId = draftPost.Id;
            task.ResultResourceId = primaryUploaded.ResourceId;
            task.ResultPresignedUrl = primaryUploaded.PresignedUrl;
            task.ResultResourceIdsJson = JsonSerializer.Serialize(resultResourceIds);
            task.ResultPresignedUrlsJson = JsonSerializer.Serialize(resultPresignedUrls);
            task.ResultCaption = caption;
            task.ResultReferencesJson = SerializeReferences(rag.References);
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            await _taskRepository.SaveChangesAsync(ct);
            await MarkSpendRecordDebitedAsync(task.Id, ct);

            await context.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    msg.UserId,
                    NotificationTypes.AiDraftPostGenerationCompleted,
                    "Draft post is ready",
                    $"Your AI-generated draft post (caption + {mediaType}) is ready.",
                    new
                    {
                        correlationId = task.CorrelationId,
                        socialMediaId = task.SocialMediaId,
                        draftPostId = task.ResultPostId,
                        postId = task.ResultPostId,
                        resourceId = task.ResultResourceId,
                        presignedUrl = task.ResultPresignedUrl,
                        resourceIds = resultResourceIds,
                        presignedUrls = resultPresignedUrls,
                        resultResourceIds,
                        resultPresignedUrls,
                        mediaType,
                        imageCount = task.ImageCount,
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
            task.Status = DraftPostTaskStatuses.Failed;
            task.ErrorCode = ex.GetType().Name;
            task.ErrorMessage = ex.Message;
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;

            await PublishErrorThinkingAsync(
                context,
                task,
                "generation_failed",
                "AI recommendation failed",
                "AI hit an error and stopped generating the recommendation draft.",
                ex,
                new
                {
                    socialMediaId = task.SocialMediaId,
                    workspaceId = task.WorkspaceId,
                    draftPostId = failedDraftPostId,
                    postId = failedDraftPostId,
                    mediaType = task.MediaType,
                    postFinalized,
                },
                ct);

            // Keep the upfront empty Post visible as a failed draft when processing
            // stops before Step 6. This lets the FE put the item in Product > Failed
            // instead of leaving a missing/processing placeholder.
            if (!postFinalized && task.ResultPostId.HasValue)
            {
                try
                {
                    var failedPost = await _postRepository.GetByIdForUpdateAsync(task.ResultPostId.Value, ct);
                    if (failedPost != null && failedPost.DeletedAt is null)
                    {
                        failedPost.Status = "failed";
                        failedPost.UpdatedAt = task.CompletedAt;
                        await _postRepository.SaveChangesAsync(ct);
                        _logger.LogInformation(
                            "DraftPost {Id}: marked placeholder Post {PostId} as failed",
                            task.Id, failedPost.Id);
                    }
                }
                catch (Exception markPostFailedEx)
                {
                    _logger.LogWarning(markPostFailedEx,
                        "DraftPost {Id}: failed to mark placeholder Post {PostId} as failed",
                        task.Id, task.ResultPostId.Value);
                }
            }

            // Keep ResultPostId on failed tasks. The FE may only know that
            // pre-created post id, and still needs to resolve the failed task plus
            // its notification timeline.
            try
            {
                await _taskRepository.SaveChangesAsync(ct);
                await RefundSpendRecordAsync(msg.UserId, task.Id, ct);
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
                            mediaType = task.MediaType,
                            errorCode = task.ErrorCode,
                            errorMessage = task.ErrorMessage,
                            details = new
                            {
                                exception = Truncate(ex.ToString(), 6000),
                                postFinalized,
                            },
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

    private async Task MarkSpendRecordDebitedAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var records = await _aiSpendRecordRepository.GetByReferenceAsync(
            CoinReferenceTypes.DraftPostGeneration,
            taskId.ToString(),
            cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        var updatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var record in records)
        {
            if (string.Equals(record.Status, AiSpendStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                record.Status = AiSpendStatuses.Debited;
                record.UpdatedAt = updatedAt;
                _aiSpendRecordRepository.Update(record);
            }
        }

        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task RefundSpendRecordAsync(Guid userId, Guid taskId, CancellationToken cancellationToken)
    {
        var records = await _aiSpendRecordRepository.GetByReferenceAsync(
            CoinReferenceTypes.DraftPostGeneration,
            taskId.ToString(),
            cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        var updatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var record in records)
        {
            if (string.Equals(record.Status, AiSpendStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var refund = await _billingClient.RefundAsync(
                userId,
                record.TotalCoins,
                CoinDebitReasons.DraftPostGenerationRefund,
                CoinReferenceTypes.DraftPostGeneration,
                taskId.ToString(),
                cancellationToken);
            if (refund.IsFailure)
            {
                _logger.LogWarning(
                    "DraftPost {TaskId}: failed to refund spend record {SpendRecordId}: {Code} {Message}",
                    taskId,
                    record.Id,
                    refund.Error.Code,
                    refund.Error.Description);
                continue;
            }

            record.Status = AiSpendStatuses.Refunded;
            record.UpdatedAt = updatedAt;
            _aiSpendRecordRepository.Update(record);
        }

        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);
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
    /// Brave image-search fan-out is bounded because it is a paid API. The final
    /// reference count is still controlled by maxReferenceImages; these constants
    /// only size the candidate pool before rerank/source allocation.
    /// </summary>
    private const int MaxFreshTopicImageQueries = 8;
    private const int MaxFreshTopicImagesPerQuery = 3;
    private const int MaxFreshTopicCandidatePool = 16;
    private const int MaxReferenceSelectorCandidateImages = 24;
    private const int ReferenceSelectorMaxOutputTokens = 700;

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

    private static ReferenceImageSlotAllocation AllocateReferenceImageSlots(string mediaType, int cap)
    {
        cap = Math.Max(0, cap);
        if (cap == 0)
        {
            return new ReferenceImageSlotAllocation(0, 0);
        }

        if (string.Equals(mediaType, DraftPostMediaTypes.Video, StringComparison.Ordinal))
        {
            var webSlots = Math.Min(2, cap);
            var ragSlots = Math.Min(1, Math.Max(0, cap - webSlots));
            return new ReferenceImageSlotAllocation(webSlots, ragSlots);
        }

        var imageWebSlots = (int)Math.Round(cap * 0.4, MidpointRounding.AwayFromZero);
        if (cap > 1 && imageWebSlots == 0) imageWebSlots = 1;
        if (cap > 1 && imageWebSlots >= cap) imageWebSlots = cap - 1;
        imageWebSlots = Math.Clamp(imageWebSlots, 0, cap);
        return new ReferenceImageSlotAllocation(imageWebSlots, cap - imageWebSlots);
    }

    private static int CalculateFreshTopicCandidateLimit(int webReferenceSlots)
    {
        if (webReferenceSlots <= 0)
        {
            return 0;
        }

        return Math.Clamp(webReferenceSlots * 3, webReferenceSlots, MaxFreshTopicCandidatePool);
    }

    private static IReadOnlyList<string> BuildFreshTopicImageQueries(
        string? refImageQuery,
        Application.Recommendations.Services.QueryRewriteResult rewrite,
        string recommendationQuery,
        string? userPrompt)
    {
        var queries = new List<string?>();
        queries.AddRange(rewrite.VisualQueries);
        queries.AddRange(ExtractExternalVisualReferenceQueries(userPrompt));

        var normalized = NormalizeQueryList(queries, MaxFreshTopicImageQueries);
        if (normalized.Count > 0)
        {
            return normalized;
        }

        // Last-resort fallback only. Keep this separate from the normal path so a
        // good LLM-produced web-query list is not polluted by the raw user prompt.
        return NormalizeQueryList(
            new[]
            {
                rewrite.VisualQuery,
                BuildCleanFallbackWebImageQuery(refImageQuery),
                BuildCleanFallbackWebImageQuery(recommendationQuery),
            },
            MaxFreshTopicImageQueries);
    }

    private static IReadOnlyList<string> ExtractExternalVisualReferenceQueries(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Array.Empty<string>();
        }

        var queries = new List<string>();
        foreach (Match match in Regex.Matches(
                     prompt,
                     @"(?:latest\s+)?(?:mv|music\s+video)\s+(?:of|by|from)\s+(?<artist>[^:;\r\n,.]+)\s*[:\-]\s*(?<title>[^.;\r\n]+)",
                     RegexOptions.IgnoreCase))
        {
            var artist = CleanSearchPhrase(match.Groups["artist"].Value);
            var title = CleanSearchPhrase(match.Groups["title"].Value);
            if (artist.Length == 0 || title.Length == 0) continue;

            queries.Add($"{artist} {title} MV");
            queries.Add($"{title} {artist} music video");
        }

        return NormalizeQueryList(queries, 3);
    }

    private static IReadOnlyList<string> NormalizeQueryList(IEnumerable<string?> values, int maxItems)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queries = new List<string>();
        foreach (var value in values)
        {
            var query = CleanSearchPhrase(value);
            if (query.Length == 0) continue;
            if (!seen.Add(query)) continue;
            queries.Add(query);
            if (queries.Count >= maxItems) break;
        }
        return queries;
    }

    private static string? BuildCleanFallbackWebImageQuery(string? value)
    {
        var query = CleanSearchPhrase(value);
        if (query.Length == 0)
        {
            return null;
        }

        return LooksLikePromptInstruction(query) ? null : query;
    }

    private static bool LooksLikePromptInstruction(string query)
    {
        return Regex.IsMatch(
            query,
            @"^\s*(please\s+)?(create|generate|make|write|draft)\s+(me\s+)?",
            RegexOptions.IgnoreCase);
    }

    private static string CleanSearchPhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var query = StripMarkdownInline(value).Trim().Trim('"', '\'', '.', '!', '?', ':', ';', ',');
        query = Regex.Replace(query, @"\s+", " ");
        query = Regex.Replace(
            query,
            @"^\s*(please\s+)?(create|generate|make|write|draft)\s+(me\s+)?(a\s+|an\s+)?((short|social|marketing|promo|promotional)\s+)?(video|image|post|content|article|caption|piece|ad|advertisement)\s*(about|on|for|of)?\s*",
            "",
            RegexOptions.IgnoreCase).Trim();
        query = Regex.Replace(
            query,
            @"\s*,?\s*(introduce|showcase|promote|write|create|generate|make)\b.*$",
            "",
            RegexOptions.IgnoreCase).Trim();
        return query.Length <= 120 ? query : query[..120];
    }

    /// <summary>
    /// Mirrors fresh-topic image hits into user resources when possible, so downstream
    /// multimodal providers get stable, fetchable URLs instead of hot-linked search
    /// result CDN URLs.
    /// </summary>
    private async Task<FreshTopicImageMirrorOutcome> MirrorFreshTopicImageHitsAsync(
        Guid userId,
        Guid? workspaceId,
        IReadOnlyList<Application.Abstractions.Search.ImageSearchHit> hits,
        CancellationToken cancellationToken)
    {
        if (hits.Count == 0)
        {
            return new FreshTopicImageMirrorOutcome(
                Array.Empty<Application.Abstractions.Search.ImageSearchHit>(),
                Array.Empty<object>(),
                Array.Empty<object>());
        }

        var mirroredHits = new List<Application.Abstractions.Search.ImageSearchHit>(hits.Count);
        var mirrors = new List<object>();
        var failures = new List<object>();

        foreach (var hit in hits)
        {
            var originalUrl = hit.ImageUrl;
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                mirroredHits.Add(hit);
                continue;
            }

            try
            {
                var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                    userId: userId,
                    urls: new[] { originalUrl },
                    status: "reference",
                    resourceType: "image",
                    cancellationToken: cancellationToken,
                    workspaceId: workspaceId,
                    provenance: new ResourceProvenanceMetadata(
                        OriginKind: ResourceOriginKinds.AiImportedUrl,
                        OriginSourceUrl: originalUrl));

                var uploaded = uploadResult.IsSuccess ? uploadResult.Value.FirstOrDefault() : null;
                if (uploaded is null || string.IsNullOrWhiteSpace(uploaded.PresignedUrl))
                {
                    failures.Add(new
                    {
                        originalUrl,
                        title = hit.Title,
                        errorCode = uploadResult.Error?.Code,
                        errorMessage = uploadResult.Error?.Description,
                    });
                    mirroredHits.Add(hit);
                    continue;
                }

                mirrors.Add(new
                {
                    originalUrl,
                    mirroredUrl = uploaded.PresignedUrl,
                    resourceId = uploaded.ResourceId,
                    contentType = uploaded.ContentType,
                    title = hit.Title,
                    sourcePageUrl = hit.SourcePageUrl,
                });
                mirroredHits.Add(hit with { ImageUrl = uploaded.PresignedUrl });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Fresh-topic image mirror failed for url={Url}; keeping original URL",
                    originalUrl);
                failures.Add(new
                {
                    originalUrl,
                    title = hit.Title,
                    errorCode = ex.GetType().Name,
                    errorMessage = ex.Message,
                });
                mirroredHits.Add(hit);
            }
        }

        return new FreshTopicImageMirrorOutcome(mirroredHits, mirrors, failures);
    }

    /// <summary>
    /// Fires Brave image search for multiple targeted visual queries, dedupes results,
    /// and returns a bounded candidate pool. Returns empty on blank queries, no API
    /// key configured, or transport errors; a search failure must not drop the draft.
    /// </summary>
    private async Task<FreshTopicImageSearchOutcome> FetchFreshTopicImageHitsAsync(
        IReadOnlyList<string> queries,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var normalizedQueries = NormalizeQueryList(queries, MaxFreshTopicImageQueries);
        if (normalizedQueries.Count == 0 || maxResults <= 0)
        {
            return new FreshTopicImageSearchOutcome(
                Array.Empty<Application.Abstractions.Search.ImageSearchHit>(),
                Queries: normalizedQueries,
                QueryOutcomes: Array.Empty<FreshTopicImageQueryOutcome>());
        }

        var allHits = new List<Application.Abstractions.Search.ImageSearchHit>();
        var outcomes = new List<FreshTopicImageQueryOutcome>();
        Exception? firstError = null;
        var perQueryLimit = Math.Clamp(
            (int)Math.Ceiling(maxResults / (double)Math.Max(normalizedQueries.Count, 1)),
            1,
            MaxFreshTopicImagesPerQuery);

        foreach (var query in normalizedQueries)
        {
            try
            {
                var hits = await _imageSearchClient.SearchImagesAsync(
                    query, perQueryLimit, cancellationToken);
                var filteredHits = hits
                    .Where(h => !string.IsNullOrWhiteSpace(h.ImageUrl))
                    .ToList();
                allHits.AddRange(filteredHits);
                outcomes.Add(new FreshTopicImageQueryOutcome(query, filteredHits.Count));
            }
            catch (Exception ex)
            {
                firstError ??= ex;
                _logger.LogWarning(ex, "Fresh-topic image search failed for query='{Query}'", query);
                outcomes.Add(new FreshTopicImageQueryOutcome(
                    query,
                    HitCount: 0,
                    ErrorCode: ex.GetType().Name,
                    ErrorMessage: ex.Message));
            }
        }

        var dedupedHits = new List<Application.Abstractions.Search.ImageSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in allHits)
        {
            var key = !string.IsNullOrWhiteSpace(hit.ImageUrl)
                ? hit.ImageUrl
                : hit.SourcePageUrl ?? hit.Title ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!seen.Add(key)) continue;
            dedupedHits.Add(hit);
            if (dedupedHits.Count >= maxResults) break;
        }

        return new FreshTopicImageSearchOutcome(
            dedupedHits,
            firstError,
            normalizedQueries,
            outcomes);
    }

    /// <summary>
    /// One image-reference candidate going into the rerank pool. <see cref="DescriptiveText"/>
    /// is what the reranker scores against the query — for past posts that's the post
    /// caption, for fresh-topic hits that's the search-result title.
    /// </summary>
    private sealed record ImageRefCandidate(
        string ImageUrl,
        string Source,
        string DescriptiveText,
        string? Title = null,
        string? SourcePageUrl = null,
        string? SearchQuery = null);

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
    private async Task<ReferenceImageSelectionOutcome> SelectReferenceImagesAsync(
        Guid taskId,
        IReadOnlyList<ImageRefCandidate> candidates,
        string? topic,
        string caption,
        string? visualQuery,                    // ← K4 enhancement: rewriter's visual_query
        IReadOnlyList<string>? keyTerms,        // ← K4 enhancement: rewriter's key_terms
        int cap,
        ReferenceImageSlotAllocation allocation,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            _logger.LogInformation("DraftPost {Id}: rerank skipped — empty candidate pool", taskId);
            return new ReferenceImageSelectionOutcome(new List<SelectedReferenceImage>(), Strategy: "none");
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
            return new ReferenceImageSelectionOutcome(
                SelectReferencesByAllocation(
                    candidates.Select((candidate, index) => new ScoredImageRefCandidate(candidate, null, index + 1)).ToList(),
                    cap,
                    allocation),
                Strategy: "candidate-order");
        }

        return await SelectReferencesBySourceAsync(
            taskId,
            candidates,
            query,
            topic,
            caption,
            visualQuery,
            keyTerms,
            cap,
            allocation,
            cancellationToken);
    }

    private async Task<ReferenceImageSelectionOutcome> SelectReferencesBySourceAsync(
        Guid taskId,
        IReadOnlyList<ImageRefCandidate> candidates,
        string query,
        string? topic,
        string caption,
        string? visualQuery,
        IReadOnlyList<string>? keyTerms,
        int cap,
        ReferenceImageSlotAllocation allocation,
        CancellationToken cancellationToken)
    {
        var freshCandidates = candidates
            .Where(candidate => IsFreshTopicCandidate(candidate.Source))
            .ToList();
        var ragCandidates = candidates
            .Where(candidate => !IsFreshTopicCandidate(candidate.Source))
            .ToList();
        var selected = new List<SelectedReferenceImage>();
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strategyParts = new List<string>();
        Exception? error = null;

        var webLimit = Math.Min(allocation.Web, cap);
        if (webLimit > 0 && freshCandidates.Count > 0)
        {
            var webSelection = await TrySelectReferenceImagesWithLlmAsync(
                taskId,
                freshCandidates,
                query,
                topic,
                caption,
                visualQuery,
                keyTerms,
                webLimit,
                new ReferenceImageSlotAllocation(webLimit, 0),
                cancellationToken);
            if (webSelection is not null && webSelection.SelectedImages.Count > 0)
            {
                AddSelected(webSelection.SelectedImages, webLimit);
                strategyParts.Add("web-llm");
            }
            else
            {
                AddSelected(SelectReferencesByCandidateOrder(freshCandidates, webLimit), webLimit);
                strategyParts.Add("web-candidate-order-fallback");
            }
        }

        var ragLimit = Math.Min(allocation.Rag, cap - selected.Count);
        if (ragLimit > 0 && ragCandidates.Count > 0)
        {
            var ragSelection = await SelectReferenceImagesWithRerankAsync(
                taskId,
                ragCandidates,
                query,
                ragLimit,
                "rag",
                cancellationToken);
            AddSelected(ragSelection.SelectedImages, ragLimit);
            strategyParts.Add(ragSelection.Strategy);
            error ??= ragSelection.Error;
        }

        if (selected.Count < cap)
        {
            var remainingRag = ragCandidates
                .Where(candidate => !usedUrls.Contains(candidate.ImageUrl))
                .ToList();
            if (remainingRag.Count > 0)
            {
                var fill = await SelectReferenceImagesWithRerankAsync(
                    taskId,
                    remainingRag,
                    query,
                    cap - selected.Count,
                    "rag-fill",
                    cancellationToken);
                AddSelected(fill.SelectedImages, cap - selected.Count);
                strategyParts.Add(fill.Strategy);
                error ??= fill.Error;
            }
        }

        if (selected.Count < cap)
        {
            var remainingFresh = freshCandidates
                .Where(candidate => !usedUrls.Contains(candidate.ImageUrl))
                .ToList();
            if (remainingFresh.Count > 0)
            {
                var webFill = await TrySelectReferenceImagesWithLlmAsync(
                    taskId,
                    remainingFresh,
                    query,
                    topic,
                    caption,
                    visualQuery,
                    keyTerms,
                    cap - selected.Count,
                    new ReferenceImageSlotAllocation(cap - selected.Count, 0),
                    cancellationToken);
                if (webFill is not null && webFill.SelectedImages.Count > 0)
                {
                    AddSelected(webFill.SelectedImages, cap - selected.Count);
                    strategyParts.Add("web-llm-fill");
                }
                else
                {
                    AddSelected(SelectReferencesByCandidateOrder(remainingFresh, cap - selected.Count), cap - selected.Count);
                    strategyParts.Add("web-candidate-order-fill");
                }
            }
        }

        if (selected.Count == 0)
        {
            selected.AddRange(SelectReferencesByCandidateOrder(candidates, cap));
            strategyParts.Add("candidate-order");
        }

        return new ReferenceImageSelectionOutcome(
            selected.Take(cap).ToList(),
            error,
            string.Join("+", strategyParts.Distinct(StringComparer.OrdinalIgnoreCase)));

        void AddSelected(IEnumerable<SelectedReferenceImage> references, int maxCount)
        {
            foreach (var reference in references)
            {
                if (selected.Count >= cap || maxCount <= 0)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(reference.ImageUrl))
                {
                    continue;
                }

                if (!usedUrls.Add(reference.ImageUrl))
                {
                    continue;
                }

                selected.Add(reference);
                maxCount--;
            }
        }
    }

    private async Task<ReferenceImageSelectionOutcome> SelectReferenceImagesWithRerankAsync(
        Guid taskId,
        IReadOnlyList<ImageRefCandidate> candidates,
        string query,
        int cap,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        if (cap <= 0 || candidates.Count == 0)
        {
            return new ReferenceImageSelectionOutcome(new List<SelectedReferenceImage>(), Strategy: $"{sourceLabel}-rerank");
        }

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
            _logger.LogWarning(
                ex,
                "DraftPost {Id}: {SourceLabel} rerank threw; falling back to candidate order",
                taskId,
                sourceLabel);
            return new ReferenceImageSelectionOutcome(
                SelectReferencesByCandidateOrder(candidates, cap),
                ex,
                Strategy: $"{sourceLabel}-candidate-order");
        }

        if (scored.Count == 0)
        {
            _logger.LogWarning(
                "DraftPost {Id}: {SourceLabel} rerank returned 0 results for {DocCount} docs; falling back to candidate order",
                taskId,
                sourceLabel,
                docs.Count);
            return new ReferenceImageSelectionOutcome(
                SelectReferencesByCandidateOrder(candidates, cap),
                Strategy: $"{sourceLabel}-candidate-order");
        }

        var ordered = scored.OrderByDescending(r => r.Score).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            if (r.Index < 0 || r.Index >= candidates.Count) continue;
            var c = candidates[r.Index];
            _logger.LogInformation(
                "DraftPost {Id}: {SourceLabel} rerank rank {Rank}/{Total} score={Score:F3} src={Source} url={Url} doc=\"{Doc}\"",
                taskId, sourceLabel, i + 1, ordered.Count, r.Score, c.Source,
                c.ImageUrl[..Math.Min(c.ImageUrl.Length, 100)],
                c.DescriptiveText[..Math.Min(c.DescriptiveText.Length, 120)]);
        }

        var kept = ordered
            .Where(r => r.Score >= RerankRelevanceThreshold && r.Index >= 0 && r.Index < candidates.Count)
            .Select((r, index) => new ScoredImageRefCandidate(
                candidates[r.Index],
                r.Score,
                index + 1))
            .Take(cap)
            .Select(candidate => ToSelectedReferenceImage(candidate))
            .ToList();

        _logger.LogInformation(
            "DraftPost {Id}: {SourceLabel} rerank kept {Kept}/{Total} (threshold={Threshold:F2}, cap={Cap})",
            taskId,
            sourceLabel,
            kept.Count,
            ordered.Count,
            RerankRelevanceThreshold,
            cap);

        return new ReferenceImageSelectionOutcome(kept, Strategy: $"{sourceLabel}-rerank");
    }

    private async Task<ReferenceImageSelectionOutcome?> TrySelectReferenceImagesWithLlmAsync(
        Guid taskId,
        IReadOnlyList<ImageRefCandidate> candidates,
        string query,
        string? topic,
        string caption,
        string? visualQuery,
        IReadOnlyList<string>? keyTerms,
        int cap,
        ReferenceImageSlotAllocation allocation,
        CancellationToken cancellationToken)
    {
        if (cap <= 0)
        {
            return new ReferenceImageSelectionOutcome(new List<SelectedReferenceImage>(), Strategy: "llm");
        }

        var indexedCandidates = candidates
            .Select((candidate, originalIndex) => new { Candidate = candidate, OriginalIndex = originalIndex })
            .Where(item => !string.IsNullOrWhiteSpace(item.Candidate.ImageUrl))
            .Take(MaxReferenceSelectorCandidateImages)
            .Select((item, displayIndex) => new IndexedImageRefCandidate(displayIndex + 1, item.OriginalIndex, item.Candidate))
            .ToList();
        if (indexedCandidates.Count == 0)
        {
            return null;
        }

        try
        {
            var selectorResult = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: ReferenceImageSelectorSystemPrompt,
                    UserText: BuildReferenceImageSelectorUserText(
                        indexedCandidates,
                        query,
                        topic,
                        caption,
                        visualQuery,
                        keyTerms,
                        cap,
                        allocation),
                    ReferenceImageUrls: indexedCandidates.Select(candidate => candidate.Candidate.ImageUrl).ToList(),
                    MaxOutputTokens: ReferenceSelectorMaxOutputTokens,
                    WebSearchEnabled: false),
                cancellationToken);

            var payload = ExtractJsonPayload(selectorResult.Answer ?? string.Empty);
            if (string.IsNullOrWhiteSpace(payload))
            {
                _logger.LogWarning(
                    "DraftPost {Id}: reference image LLM selector returned no JSON; falling back to rerank",
                    taskId);
                return null;
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!TryGetJsonProperty(root, "selected", out var selectedElement) ||
                selectedElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "DraftPost {Id}: reference image LLM selector JSON has no selected array; falling back to rerank",
                    taskId);
                return null;
            }

            var selected = new List<SelectedReferenceImage>();
            var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pick in selectedElement.EnumerateArray())
            {
                if (selected.Count >= cap)
                {
                    break;
                }

                if (pick.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var candidateNumber = ReadJsonInt(
                    pick,
                    "candidate_number",
                    "candidateNumber",
                    "candidate_index",
                    "candidateIndex",
                    "index");
                if (candidateNumber is null)
                {
                    continue;
                }

                var indexed = indexedCandidates.FirstOrDefault(candidate => candidate.CandidateNumber == candidateNumber.Value);
                if (indexed is null && candidateNumber.Value >= 0 && candidateNumber.Value < indexedCandidates.Count)
                {
                    // Tolerate a zero-based index if the model ignores the schema.
                    indexed = indexedCandidates[candidateNumber.Value];
                }

                if (indexed is null)
                {
                    continue;
                }

                var candidate = indexed.Candidate;
                if (!usedUrls.Add(candidate.ImageUrl))
                {
                    continue;
                }

                var coverage = TruncateOneLine(ReadJsonString(pick, "coverage", "covers"), 180);
                var reason = TruncateOneLine(ReadJsonString(pick, "reason", "selection_reason", "selectionReason"), 240);
                selected.Add(ToSelectedReferenceImage(
                    new ScoredImageRefCandidate(candidate, null, selected.Count + 1),
                    coverage,
                    reason));
            }

            if (selected.Count == 0)
            {
                _logger.LogWarning(
                    "DraftPost {Id}: reference image LLM selector picked no valid candidates; falling back to rerank",
                    taskId);
                return null;
            }

            _logger.LogInformation(
                "DraftPost {Id}: reference image LLM selector kept {Kept}/{VisibleCandidates} (cap={Cap}, webSlots={WebSlots}, ragSlots={RagSlots})",
                taskId, selected.Count, indexedCandidates.Count, cap, allocation.Web, allocation.Rag);

            return new ReferenceImageSelectionOutcome(selected, Strategy: "llm");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DraftPost {Id}: reference image LLM selector failed; falling back to rerank", taskId);
            return null;
        }
    }

    private static string BuildReferenceImageSelectorUserText(
        IReadOnlyList<IndexedImageRefCandidate> candidates,
        string query,
        string? topic,
        string caption,
        string? visualQuery,
        IReadOnlyList<string>? keyTerms,
        int cap,
        ReferenceImageSlotAllocation allocation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Select the best visual reference images for the next AI-generated social post.");
        sb.AppendLine($"Maximum images to select: {cap}");
        sb.AppendLine($"Source mix guidance: fresh web images={allocation.Web}, past-post/RAG images={allocation.Rag}. Use this as guidance only; coverage of requested subjects is more important.");
        sb.AppendLine("Candidate N maps to attached image N. Return candidate_number values from this numbered list.");
        sb.AppendLine("For web-search candidates, evaluate attached image N together with its web_search_text fields. Use the originating search_query and result title/source to identify whether the image matches the user's requested subject/entity.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(topic))
        {
            sb.AppendLine("Topic/search intent:");
            sb.AppendLine(TruncateOneLine(topic, 500));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(visualQuery))
        {
            sb.AppendLine("Visual query:");
            sb.AppendLine(TruncateOneLine(visualQuery, 500));
            sb.AppendLine();
        }

        if (keyTerms is { Count: > 0 })
        {
            sb.AppendLine("Key terms/entities:");
            sb.AppendLine(string.Join(", ", keyTerms.Take(12)));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            sb.AppendLine("Draft caption:");
            sb.AppendLine(TruncateOneLine(caption, 900));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            sb.AppendLine("Combined selection query:");
            sb.AppendLine(TruncateOneLine(query, 1200));
            sb.AppendLine();
        }

        sb.AppendLine("Candidates:");
        foreach (var indexed in candidates)
        {
            var candidate = indexed.Candidate;
            var title = TruncateOneLine(candidate.Title, 160);
            var searchQuery = TruncateOneLine(candidate.SearchQuery, 160);
            var sourcePage = TruncateOneLine(candidate.SourcePageUrl, 180);
            var description = TruncateOneLine(candidate.DescriptiveText, 260);
            sb.Append(indexed.CandidateNumber)
                .Append(". source=")
                .Append(candidate.Source);
            if (!string.IsNullOrWhiteSpace(title))
            {
                sb.Append("; web_search_title=\"").Append(title).Append('"');
            }
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                sb.Append("; web_search_query=\"").Append(searchQuery).Append('"');
            }
            if (!string.IsNullOrWhiteSpace(sourcePage))
            {
                sb.Append("; web_search_source_page=\"").Append(sourcePage).Append('"');
            }
            if (!string.IsNullOrWhiteSpace(description))
            {
                sb.Append("; web_search_text=\"").Append(description).Append('"');
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Return JSON only. Pick a balanced set, not just the highest-quality-looking photos.");
        return sb.ToString();
    }

    private static List<SelectedReferenceImage> SelectReferencesByCandidateOrder(
        IReadOnlyList<ImageRefCandidate> candidates,
        int cap)
    {
        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ImageUrl))
            .DistinctBy(candidate => candidate.ImageUrl, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, cap))
            .Select((candidate, index) => ToSelectedReferenceImage(new ScoredImageRefCandidate(candidate, null, index + 1)))
            .ToList();
    }

    private static List<SelectedReferenceImage> SelectReferencesByAllocation(
        IReadOnlyList<ScoredImageRefCandidate> rankedCandidates,
        int cap,
        ReferenceImageSlotAllocation allocation)
    {
        var selected = new List<ScoredImageRefCandidate>();
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidates(rankedCandidates.Where(candidate => IsFreshTopicCandidate(candidate.Candidate.Source)), allocation.Web);
        AddCandidates(rankedCandidates.Where(candidate => !IsFreshTopicCandidate(candidate.Candidate.Source)), allocation.Rag);
        AddCandidates(rankedCandidates, cap - selected.Count);

        return selected
            .OrderBy(candidate => candidate.Rank)
            .Take(cap)
            .Select(candidate => ToSelectedReferenceImage(candidate))
            .ToList();

        void AddCandidates(IEnumerable<ScoredImageRefCandidate> candidatesToAdd, int count)
        {
            if (count <= 0) return;

            foreach (var candidate in candidatesToAdd)
            {
                if (selected.Count >= cap) return;
                if (count <= 0) return;
                if (string.IsNullOrWhiteSpace(candidate.Candidate.ImageUrl)) continue;
                if (!usedUrls.Add(candidate.Candidate.ImageUrl)) continue;

                selected.Add(candidate);
                count--;
            }
        }
    }

    private static bool IsFreshTopicCandidate(string source)
    {
        return source.StartsWith("fresh-topic", StringComparison.OrdinalIgnoreCase) ||
               source.Equals("web", StringComparison.OrdinalIgnoreCase) ||
               source.Equals("web-search", StringComparison.OrdinalIgnoreCase);
    }

    private static SelectedReferenceImage ToSelectedReferenceImage(
        ScoredImageRefCandidate scored,
        string? coverage = null,
        string? selectionReason = null)
    {
        var candidate = scored.Candidate;
        return new SelectedReferenceImage(
            ImageUrl: candidate.ImageUrl,
            Source: candidate.Source,
            DescriptiveText: candidate.DescriptiveText,
            Title: candidate.Title,
            SourcePageUrl: candidate.SourcePageUrl,
            SearchQuery: candidate.SearchQuery,
            Score: scored.Score,
            Rank: scored.Rank,
            Coverage: coverage,
            SelectionReason: selectionReason);
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
    private async Task<StyleKnowledgeOutcome> FetchStyleKnowledgeAsync(string style, string language, CancellationToken cancellationToken)
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
            return new StyleKnowledgeOutcome(resp.Answer ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch style knowledge for style={Style} — proceeding with brief-prompt addendum only",
                style);
            return new StyleKnowledgeOutcome(string.Empty, ex);
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + $"…[truncated {value.Length - max} more chars]";
    }

    private static string TruncateOneLine(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + "...";
    }

    private static string BuildImagePrompt(string userPrompt)
        => $"Generate an image for a social media post on this topic: {userPrompt}.\n\n" +
           "Match the visual style of the attached reference images: same palette, same lighting, " +
           "similar composition. " +
           ReferenceImageSimilarityGuard +
           "Keep it brand-consistent.";

    private async Task<GeneratedDraftMedia> GenerateRecommendationVideoAsync(
        string prompt,
        IReadOnlyList<string> referenceImageUrls,
        CancellationToken cancellationToken)
    {
        var normalizedReferences = referenceImageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxVideoReferenceImages)
            .ToList();
        var generationType = normalizedReferences.Count > 0 ? "REFERENCE_2_VIDEO" : null;
        var submitResult = await _veoVideoService.GenerateVideoAsync(
            new VeoGenerateRequest(
                Prompt: prompt,
                ImageUrls: normalizedReferences,
                Model: RecommendationVideoModel,
                GenerationType: generationType,
                AspectRatio: RecommendationVideoAspectRatio,
                Variant: RecommendationVideoVariant,
                Resolution: RecommendationVideoResolution,
                Duration: RecommendationVideoDurationSeconds,
                UseCallback: false),
            cancellationToken);

        if (!submitResult.Success || string.IsNullOrWhiteSpace(submitResult.TaskId))
        {
            throw new InvalidOperationException(
                $"Veo 3.1 Fast submission failed: {submitResult.Code} {submitResult.Message}");
        }

        var deadline = DateTime.UtcNow.Add(RecommendationVideoTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var statusResult = await _veoVideoService.GetVideoDetailsAsync(submitResult.TaskId, cancellationToken);
            if (!statusResult.Success)
            {
                throw new InvalidOperationException(
                    $"Veo 3.1 Fast status lookup failed: {statusResult.Code} {statusResult.Message}");
            }

            var status = statusResult.Data;
            if (status?.ErrorCode is { } errorCode && errorCode != 0)
            {
                throw new InvalidOperationException(
                    $"Veo 3.1 Fast generation failed: {errorCode} {status.ErrorMessage}");
            }

            if (status?.SuccessFlag == 1)
            {
                var resultUrl = status.Response?.ResultUrls?
                    .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
                if (string.IsNullOrWhiteSpace(resultUrl))
                {
                    throw new InvalidOperationException(
                        "Veo 3.1 Fast generation completed without a result URL.");
                }

                return new GeneratedDraftMedia(
                    Ordinal: 1,
                    Total: 1,
                    Url: resultUrl,
                    MimeType: "video/mp4",
                    ProviderTaskId: submitResult.TaskId,
                    Resolution: status.Response?.Resolution ?? RecommendationVideoResolution);
            }

            await Task.Delay(RecommendationVideoPollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"Veo 3.1 Fast generation did not finish within {RecommendationVideoTimeout.TotalMinutes:0} minutes.");
    }

    /// <summary>
    /// Step 3.5 — calls gpt-4o-mini with the post caption + all RAG context (text refs,
    /// video segment captions/transcripts, knowledge guidance) to produce a focused
    /// JSON brief that the selected image/video generation branch will consume.
    ///
    /// Falls back to a generic brief if the LLM call fails or produces malformed JSON
    /// — we never want a single failing brief to drop the whole draft pipeline.
    /// </summary>
    private async Task<ImageBriefOutcome> BuildImageBriefAsync(
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
            sb.AppendLine($"=== Optional image-design heuristics for STYLE = {style} (from knowledge base; apply only if relevant) ===");
            sb.AppendLine(styleKnowledge.Length > 3000 ? styleKnowledge[..3000] + "…" : styleKnowledge);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(rag.Answer))
        {
            sb.AppendLine("RAG recommendation summary (may include formulas/hooks/design heuristics; evaluate before applying):");
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
                return new ImageBriefOutcome(BuildFallbackBrief(userPrompt));
            }
            return new ImageBriefOutcome(new ImageBrief(prompt, styleNotes, aspectRatio));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image brief LLM call or JSON parse failed — falling back to generic");
            return new ImageBriefOutcome(BuildFallbackBrief(userPrompt), ex);
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

    private static string? ExtractJsonPayload(string rawAnswer)
    {
        var trimmed = StripJsonFence(rawAnswer);
        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');
        var start = objectStart >= 0 && arrayStart >= 0
            ? Math.Min(objectStart, arrayStart)
            : Math.Max(objectStart, arrayStart);
        if (start < 0)
        {
            return null;
        }

        var end = trimmed[start] == '{'
            ? trimmed.LastIndexOf('}')
            : trimmed.LastIndexOf(']');
        if (end <= start)
        {
            return null;
        }

        return trimmed[start..(end + 1)];
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static int? ReadJsonInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetJsonProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ReadJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetJsonProperty(element, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
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
