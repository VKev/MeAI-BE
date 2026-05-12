using System.Text;
using System.Text.Json;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
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
/// End-to-end async "improve this existing post" generation. Sibling to
/// <see cref="DraftPostGenerationConsumer"/>; the major differences:
///
///   * Anchors RAG retrieval on the ORIGINAL post (caption + image refs) rather
///     than a fresh user prompt — finds how this account has already written
///     about similar topics.
///   * Step 3 (caption regen) and Step 4 (image regen) are CONDITIONAL based on
///     <see cref="GenerateRecommendPostStarted.ImproveCaption"/> /
///     <see cref="GenerateRecommendPostStarted.ImproveImage"/>. Either or both.
///   * System prompts are "improve this post while preserving voice / topic"
///     rather than "recommend the next post to create".
///   * Does NOT create a new <see cref="Post"/>. The original Post is left
///     untouched; outputs are persisted on the <see cref="RecommendPost"/> row.
///   * No fresh-topic image search (Brave) and no separate rerank candidate
///     pool — the original image IS the primary visual anchor for the brief.
/// </summary>
public sealed class RecommendPostGenerationConsumer : IConsumer<GenerateRecommendPostStarted>
{
    /// <summary>
    /// Improve-caption system prompt. Anchored on the original post; the LLM
    /// rewrites it preserving voice / topic / language while applying the user's
    /// optional steering instruction.
    /// </summary>
    private const string ImproveCaptionSystemPrompt =
        "You are a social-media caption editor. The user has an EXISTING post — caption + image — " +
        "and has asked you to IMPROVE the caption. Your output replaces the existing caption.\n\n" +
        "INPUTS YOU SEE:\n" +
        "  (a) The current caption verbatim, in a section labeled '=== Current caption ==='\n" +
        "  (b) The user's optional improvement instruction (may be empty)\n" +
        "  (c) A RAG recommendation block summarizing this account's voice + content formulas, " +
        "      hooks, engagement tactics that have worked for similar topics on this account\n" +
        "  (d) The original post's image(s) attached as references\n\n" +
        "RULES:\n" +
        "  * PRESERVE the topic of the original caption — do not pivot to a different subject. " +
        "    The user wants this same post improved, not a different one.\n" +
        "  * PRESERVE the language of the original caption (auto-detect from the existing text).\n" +
        "  * PRESERVE the page name / brand voice. Match emoji density and hashtag style.\n" +
        "  * APPLY whichever formula / hook / engagement trigger the RAG block recommends, " +
        "    when it is consistent with the topic.\n" +
        "  * APPLY the user's improvement instruction faithfully when one is provided.\n" +
        "  * KEEP contact info verbatim from the page profile (URL TLDs, email locals, phone " +
        "    numbers — never rewrite or canonicalize). Drop a contact field rather than invent one.\n\n" +
        "FORMATTING — CRITICAL: The caption is rendered VERBATIM by Facebook / Instagram / " +
        "TikTok / Threads, none of which parse Markdown. Output plain text only. NO `**`/`__`/" +
        "`*`/`_` for bold/italic. NO `#`/`##`/`###` heading lines (a hashtag is `#OneWord` with no " +
        "space, NOT `# heading`). NO `-`/`*`/`>` markdown bullets. NO `[text](url)` links. NO " +
        "code backticks. For lists use emoji bullets or plain numbered lines. Hashtags go on " +
        "their own line(s) at the end (or interleaved if the original did that). Bare URLs auto-render.\n\n" +
        "Do NOT use web search. Output the improved caption ONLY — no preface, no explanation, " +
        "no Markdown.";

    /// <summary>
    /// Image-brief system prompt for the improve flow. Inputs the LLM with the
    /// existing image + the (existing or improved) caption + style-knowledge, and
    /// asks it to author a brief for a NEW image that improves on the existing one.
    /// </summary>
    private const string ImproveImageBriefSystemPrompt =
        "You are an art director authoring a brief for a NEW image that IMPROVES on an existing " +
        "social-media post image. The user has the existing post and wants the image regenerated " +
        "while staying recognizably on-topic.\n\n" +
        "INPUTS YOU SEE:\n" +
        "  (a) The post's caption (current or just-improved) — that's what the new image must illustrate\n" +
        "  (b) The user's optional improvement instruction for the image\n" +
        "  (c) The original image attached as a reference for subject / palette / brand identity\n" +
        "  (d) Style-knowledge for the requested style ('creative' / 'branded' / 'marketing'), " +
        "      describing on-image text rules + visual conventions for that style\n\n" +
        "RULES:\n" +
        "  * Preserve the subject of the original image — same product / scene / person — unless " +
        "    the user's instruction explicitly asks to change it.\n" +
        "  * Improve composition, lighting, palette, or text overlay quality per the style-knowledge.\n" +
        "  * IMPORTANT: The original/reference image is for reference only; do not make the new image " +
        "too similar. Use it for subject, palette, lighting, mood, and brand cues, then create a " +
        "new composition.\n" +
        "  * If the user's instruction targets the IMAGE specifically (e.g. 'cooler palette', " +
        "    'less busy', 'add product close-up'), apply it faithfully. If the instruction is " +
        "    caption-only, default to a refined version of the original visual concept.\n" +
        "  * Aspect ratio: match the original (square 1:1 default for feed; 9:16 if a reel).\n\n" +
        "OUTPUT (JSON, no Markdown):\n" +
        "  {\n" +
        "    \"prompt\": \"<concrete description for the image-gen model — subject, composition, " +
        "lighting, palette, on-image text if any (verbatim only — never invent contact info)>\",\n" +
        "    \"aspect_ratio\": \"<1:1 | 9:16 | 16:9 | 4:5>\",\n" +
        "    \"style_notes\": \"<optional brief constraints to add to the image-gen system prompt, " +
        "e.g. 'photo-real', 'flat illustration', 'no text overlay'>\"\n" +
        "  }\n\n" +
        "Output JSON ONLY — no preamble, no Markdown fence.";

    private const string ReferenceImageSimilarityGuard =
        "IMPORTANT: Reference images are for reference only; do not make the generated image " +
        "too similar to any reference image. Use them for palette, lighting, mood, and brand " +
        "cues, then create a new composition. ";

    /// <summary>
    /// Style-aware image-gen system prompt. Same shape as DraftPostGenerationConsumer's
    /// version (creative = no on-image text; branded = optional short headline; marketing
    /// = full headline + CTA + contact), kept inline so this consumer doesn't reach into
    /// the draft-post consumer's privates.
    /// </summary>
    private static string ImageSystemPromptFor(string style) => style switch
    {
        DraftPostStyles.Creative =>
            "Generate a single editorial / lifestyle photograph. NO on-image text of any kind. " +
            ReferenceImageSimilarityGuard +
            "The brand identity is carried by the visual mood + composition, not by overlays.",
        DraftPostStyles.Marketing =>
            "Generate a single promotional social-media image. Render on-image text VERBATIM as " +
            "specified in the brief — brand name, headline, value prop, CTA, contact line — using " +
            "high-contrast type that's legible at thumbnail size. " +
            ReferenceImageSimilarityGuard +
            "Treat any URL/email/phone in the brief as exact: do NOT shorten, normalize, or canonicalize.",
        _ /* Branded */ =>
            "Generate a single branded social-media image. Optional short headline overlay (<= 6 " +
            "words) at top-left or center-top — only render text if the brief explicitly includes " +
            "it. Subtle brand mark welcome (corner watermark scale). " +
            ReferenceImageSimilarityGuard +
            "Treat any URL/email/phone in the brief as exact verbatim text.",
    };

    private const int DefaultRagTopK = 6;

    private readonly IMediator _mediator;
    private readonly IRecommendPostRepository _taskRepository;
    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IRagClient _ragClient;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly IImageGenerationClient _imageGenClient;
    private readonly Application.Recommendations.Services.IQueryRewriter _queryRewriter;
    private readonly ILogger<RecommendPostGenerationConsumer> _logger;

    public RecommendPostGenerationConsumer(
        IMediator mediator,
        IRecommendPostRepository taskRepository,
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IRagClient ragClient,
        IMultimodalLlmClient multimodalLlm,
        IImageGenerationClient imageGenClient,
        Application.Recommendations.Services.IQueryRewriter queryRewriter,
        ILogger<RecommendPostGenerationConsumer> logger)
    {
        _mediator = mediator;
        _taskRepository = taskRepository;
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _ragClient = ragClient;
        _multimodalLlm = multimodalLlm;
        _imageGenClient = imageGenClient;
        _queryRewriter = queryRewriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GenerateRecommendPostStarted> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;
        var style = DraftPostStyles.NormalizeOrDefault(msg.Style);

        _logger.LogInformation(
            "ImprovePost: starting CorrelationId={CorrelationId} UserId={UserId} OriginalPostId={PostId} ImproveCaption={Caption} ImproveImage={Image} Style={Style}",
            msg.CorrelationId, msg.UserId, msg.OriginalPostId,
            msg.ImproveCaption, msg.ImproveImage, style);

        var task = await _taskRepository.GetByCorrelationIdForUpdateAsync(msg.CorrelationId, ct);
        if (task is null)
        {
            _logger.LogWarning("ImprovePost: task not found for CorrelationId={CorrelationId}", msg.CorrelationId);
            return;
        }

        try
        {
            task.Status = RecommendPostStatuses.Processing;
            task.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            await _taskRepository.SaveChangesAsync(ct);

            await PublishImproveNotificationAsync(
                context,
                msg.UserId,
                NotificationTypes.AiPostImproveProcessing,
                "Post improvement started",
                "AI is improving your post now.",
                task,
                task.UpdatedAt,
                ct);

            // ── Load original post + original resources ─────────────────────
            var originalPost = await _postRepository.GetByIdAsync(msg.OriginalPostId, ct);
            if (originalPost is null || originalPost.DeletedAt.HasValue)
            {
                throw new InvalidOperationException(
                    $"Original post {msg.OriginalPostId} not found or deleted before consumer ran.");
            }
            var originalCaption = originalPost.Content?.Content ?? string.Empty;
            var originalResourceIds = ParseResourceIds(originalPost.Content?.ResourceList);
            _logger.LogInformation(
                "ImprovePost {Id}: original captionLen={CaptionLen} chars, originalResources={ResCount}",
                task.Id, originalCaption.Length, originalResourceIds.Count);

            // ── Step 0 — WaitForRagReady (same contract as draft-post) ──────
            _logger.LogInformation("ImprovePost {Id}: waiting for RAG to be ready...", task.Id);
            await _ragClient.WaitForRagReadyAsync(ct);
            _logger.LogInformation("ImprovePost {Id}: RAG ready", task.Id);

            // ── Step 1 — re-index the social account if we have one ────────
            // Only meaningful when the post is bound to a social media account.
            // For unbound drafts (SocialMediaId = null), skip this step — the
            // RAG anchor query in Step 2 is unscoped which still finds knowledge.
            if (originalPost.SocialMediaId.HasValue && originalPost.SocialMediaId.Value != Guid.Empty)
            {
                _logger.LogDebug("ImprovePost {Id}: re-indexing posts (max=30)...", task.Id);
                var indexResult = await _mediator.Send(
                    new IndexSocialAccountPostsCommand(
                        msg.UserId,
                        originalPost.SocialMediaId.Value,
                        30),
                    ct);
                if (indexResult.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Indexing failed: {indexResult.Error.Code} {indexResult.Error.Description}");
                }
                _logger.LogInformation(
                    "ImprovePost {Id}: indexed total={Total} new={New} updated={Updated} unchanged={Unchanged}",
                    task.Id,
                    indexResult.Value.TotalPostsScanned,
                    indexResult.Value.NewPosts,
                    indexResult.Value.UpdatedPosts,
                    indexResult.Value.UnchangedPosts);
            }
            else
            {
                _logger.LogInformation(
                    "ImprovePost {Id}: skipping re-index (post has no SocialMediaId)", task.Id);
            }

            // ── Step 1.5 — Query rewriter. Single LLM call up-front; outputs feed
            // the RAG handler (Step 2) AND the downstream style-knowledge fetch
            // (Step 3.4). Threaded into the query via PrecomputedRewrite so the
            // handler doesn't repeat the LLM call.
            // The user's improvement instruction (if present) is the most informative
            // input for the rewriter — it tells us what they want changed. Original
            // caption is the fallback prompt when the user gave no instruction.
            var rewriterPrompt = !string.IsNullOrWhiteSpace(msg.UserInstruction)
                ? $"Improve an existing social post: {msg.UserInstruction}. Original caption: {originalCaption}"
                : $"Improve this social post while preserving its topic: {originalCaption}";
            var rewriteResult = await _queryRewriter.RewriteAsync(
                new Application.Recommendations.Services.QueryRewriteRequest(
                    UserPrompt: rewriterPrompt,
                    PageProfileSnippet: null,
                    Platform: null,
                    Style: style),
                ct);
            var rewrite = rewriteResult.IsSuccess
                ? rewriteResult.Value
                : new Application.Recommendations.Services.QueryRewriteResult(
                    Language: "en",
                    Intent: "informational",
                    PrimaryQuery: originalCaption,
                    AltQueries: Array.Empty<string>(),
                    VisualQuery: originalCaption,
                    KeyTerms: Array.Empty<string>());
            _logger.LogInformation(
                "ImprovePost {Id}: rewriter lang={Lang} intent={Intent} primary={Primary} visual={Visual}",
                task.Id, rewrite.Language, rewrite.Intent,
                Truncate(rewrite.PrimaryQuery, 180), Truncate(rewrite.VisualQuery, 180));

            // ── Step 2 — RAG query anchored on the original caption ────────
            // The original caption IS the topic anchor here; we pull past-post
            // examples + content formulas for voice matching, not topic discovery.
            // For unbound posts this still works — the query just runs unscoped.
            string ragAnswer = string.Empty;
            string ragReferencesJson = "[]";
            if (originalPost.SocialMediaId.HasValue && originalPost.SocialMediaId.Value != Guid.Empty
                && !string.IsNullOrWhiteSpace(originalCaption))
            {
                _logger.LogDebug("ImprovePost {Id}: querying RAG (anchor=caption, with rewriter)", task.Id);
                var queryResult = await _mediator.Send(
                    new QueryAccountRecommendationsQuery(
                        msg.UserId,
                        originalPost.SocialMediaId.Value,
                        originalCaption,
                        DefaultRagTopK,
                        PrecomputedRewrite: rewrite),
                    ct);
                if (queryResult.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"RAG query failed: {queryResult.Error.Code} {queryResult.Error.Description}");
                }
                ragAnswer = queryResult.Value.Answer ?? string.Empty;
                _logger.LogInformation(
                    "ImprovePost {Id}: RAG returned answer={AnswerLen} chars, references={RefCount}",
                    task.Id, ragAnswer.Length, queryResult.Value.References.Count);
                ragReferencesJson = SerializeReferences(queryResult.Value.References);
            }
            else
            {
                _logger.LogInformation(
                    "ImprovePost {Id}: skipping RAG account-query (no SocialMediaId or empty caption)", task.Id);
            }

            // ── Presign the original image(s) — used as references for both
            //    the caption LLM and the image-brief LLM if those steps run ──
            var originalRefImageUrls = await PresignAsync(msg.UserId, originalResourceIds, ct);
            _logger.LogInformation(
                "ImprovePost {Id}: presigned {Count} original image refs", task.Id, originalRefImageUrls.Count);

            // ── Step 3 — caption regen (CONDITIONAL on ImproveCaption) ─────
            string improvedCaption = originalCaption;
            if (msg.ImproveCaption)
            {
                var captionUserText = BuildImproveCaptionUserText(
                    originalCaption: originalCaption,
                    userInstruction: msg.UserInstruction,
                    ragAnswer: ragAnswer,
                    style: style);
                _logger.LogInformation(
                    "LLM[improveCaption] INPUT for ImprovePost {Id} Style={Style} ({UserTextLen} chars, {RefCount} ref images):\n{UserText}",
                    task.Id, style, captionUserText.Length, originalRefImageUrls.Count, Truncate(captionUserText, 4000));

                var captionResult = await _multimodalLlm.GenerateAnswerAsync(
                    new MultimodalAnswerRequest(
                        SystemPrompt: ImproveCaptionSystemPrompt,
                        UserText: captionUserText,
                        ReferenceImageUrls: originalRefImageUrls),
                    ct);
                improvedCaption = (captionResult.Answer ?? string.Empty).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(improvedCaption))
                {
                    throw new InvalidOperationException("Improve-caption LLM returned empty content.");
                }
                _logger.LogInformation(
                    "LLM[improveCaption] OUTPUT for ImprovePost {Id} ({CaptionLen} chars):\n{Caption}",
                    task.Id, improvedCaption.Length, Truncate(improvedCaption, 2000));
            }
            else
            {
                _logger.LogInformation(
                    "ImprovePost {Id}: caption regen skipped (ImproveCaption=false)", task.Id);
            }

            // ── Step 4 — image regen (CONDITIONAL on ImproveImage) ─────────
            Guid? resultResourceId = null;
            string? resultPresignedUrl = null;
            if (msg.ImproveImage)
            {
                // Step 3.4 — fetch style-knowledge for the requested style (localized)
                var styleKnowledge = await FetchStyleKnowledgeAsync(style, rewrite.Language, ct);
                _logger.LogInformation(
                    "ImprovePost {Id}: style-knowledge[{Style}] fetched ({Len} chars)",
                    task.Id, style, styleKnowledge.Length);

                // Step 4.0 — image-brief LLM (authors the prompt for image-gen)
                var briefUserText = BuildImageBriefUserText(
                    captionForImage: improvedCaption,
                    userInstruction: msg.UserInstruction,
                    styleKnowledge: styleKnowledge,
                    style: style);
                _logger.LogInformation(
                    "LLM[improveImageBrief] INPUT for ImprovePost {Id} Style={Style} ({UserTextLen} chars, {RefCount} ref images)",
                    task.Id, style, briefUserText.Length, originalRefImageUrls.Count);

                var briefResult = await _multimodalLlm.GenerateAnswerAsync(
                    new MultimodalAnswerRequest(
                        SystemPrompt: ImproveImageBriefSystemPrompt,
                        UserText: briefUserText,
                        ReferenceImageUrls: originalRefImageUrls),
                    ct);
                var brief = ParseImageBrief(briefResult.Answer ?? string.Empty);
                _logger.LogInformation(
                    "LLM[improveImageBrief] OUTPUT for ImprovePost {Id} aspect={Aspect}, prompt={PromptLen} chars, styleNotes={NotesLen} chars",
                    task.Id, brief.AspectRatio, brief.Prompt.Length, brief.StyleNotes?.Length ?? 0);

                // Step 4.1 — image generation
                var imageBaseSystem = ImageSystemPromptFor(style);
                var fullImagePrompt =
                    $"{brief.Prompt}\n\n" +
                    $"Aspect ratio: {brief.AspectRatio}. " +
                    "Render at high resolution. Output an image, no text response.";
                var fullImageSystemPrompt = string.IsNullOrWhiteSpace(brief.StyleNotes)
                    ? imageBaseSystem
                    : $"{imageBaseSystem}\n\nAdditional style constraints from the art-director brief: {brief.StyleNotes}";
                _logger.LogInformation(
                    "IMAGEGEN INPUT for ImprovePost {Id} Style={Style} ({RefCount} ref images)\n  --- prompt ---\n{Prompt}\n  --- system ---\n{System}",
                    task.Id, style, originalRefImageUrls.Count,
                    Truncate(fullImagePrompt, 2500),
                    Truncate(fullImageSystemPrompt, 1500));

                var imageResult = await _imageGenClient.GenerateImageAsync(
                    new ImageGenerationRequest(
                        Prompt: fullImagePrompt,
                        ReferenceImageUrls: originalRefImageUrls,
                        SystemPrompt: fullImageSystemPrompt),
                    ct);
                _logger.LogInformation(
                    "IMAGEGEN OUTPUT for ImprovePost {Id}: mime={MimeType}, dataUrlLen={Len}, costUsd={Cost}",
                    task.Id, imageResult.MimeType, imageResult.DataUrl.Length, imageResult.CostUsd);

                // Step 4.2 — upload to S3 via User microservice
                _logger.LogDebug("ImprovePost {Id}: uploading generated image to S3...", task.Id);
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
                resultResourceId = uploaded.ResourceId;
                resultPresignedUrl = uploaded.PresignedUrl;
            }
            else
            {
                _logger.LogInformation(
                    "ImprovePost {Id}: image regen skipped (ImproveImage=false)", task.Id);
            }

            // ── Step 5 — persist outputs on RecommendPost row, mark Completed ──
            // Note: original Post is NEVER modified.
            task.Status = RecommendPostStatuses.Completed;
            task.ResultCaption = msg.ImproveCaption ? improvedCaption : null;
            task.ResultResourceId = resultResourceId;
            task.ResultPresignedUrl = resultPresignedUrl;
            task.ResultReferencesJson = ragReferencesJson;
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            await _taskRepository.SaveChangesAsync(ct);

            await PublishImproveNotificationAsync(
                context,
                msg.UserId,
                NotificationTypes.AiPostImproveCompleted,
                "Post improvement is ready",
                "Your AI-improved post is ready to review.",
                task,
                task.CompletedAt,
                ct);

            _logger.LogInformation(
                "ImprovePost {Id}: completed CorrelationId={CorrelationId}", task.Id, task.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImprovePost {Id}: failed CorrelationId={CorrelationId}", task.Id, task.CorrelationId);

            task.Status = RecommendPostStatuses.Failed;
            task.ErrorCode = ex.GetType().Name;
            task.ErrorMessage = ex.Message;
            task.CompletedAt = DateTimeExtensions.PostgreSqlUtcNow;
            task.UpdatedAt = task.CompletedAt;
            try
            {
                await _taskRepository.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "ImprovePost {Id}: failed to persist Failed status", task.Id);
            }

            try
            {
                await PublishImproveNotificationAsync(
                    context,
                    msg.UserId,
                    NotificationTypes.AiPostImproveFailed,
                    "Post improvement failed",
                    "Your AI post improvement could not be generated. Please try again.",
                    task,
                    task.CompletedAt,
                    ct);
            }
            catch (Exception notifyEx)
            {
                _logger.LogError(notifyEx, "ImprovePost {Id}: failed to publish failure notification", task.Id);
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Task PublishImproveNotificationAsync(
        ConsumeContext<GenerateRecommendPostStarted> context,
        Guid userId,
        string type,
        string title,
        string message,
        RecommendPost task,
        DateTime? createdAt,
        CancellationToken cancellationToken)
    {
        return context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                userId,
                type,
                title,
                message,
                new
                {
                    correlationId = task.CorrelationId,
                    recommendPostId = task.Id,
                    originalPostId = task.OriginalPostId,
                    postId = task.OriginalPostId,
                    userId = userId,
                    workspaceId = task.WorkspaceId,
                    status = task.Status,
                    taskStatus = task.Status,
                    improveCaption = task.ImproveCaption,
                    improveImage = task.ImproveImage,
                    style = task.Style,
                    userInstruction = task.UserInstruction,
                    resultCaption = task.ResultCaption,
                    resultResourceId = task.ResultResourceId,
                    resultPresignedUrl = task.ResultPresignedUrl,
                    errorCode = task.ErrorCode,
                    errorMessage = task.ErrorMessage,
                    createdAt = task.CreatedAt,
                    completedAt = task.CompletedAt,
                },
                createdAt: createdAt,
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }

    private static List<Guid> ParseResourceIds(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            return new List<Guid>();
        }
        var ids = new List<Guid>(raw.Count);
        foreach (var value in raw)
        {
            if (Guid.TryParse(value, out var id) && id != Guid.Empty)
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private async Task<List<string>> PresignAsync(Guid userId, IReadOnlyList<Guid> resourceIds, CancellationToken ct)
    {
        if (resourceIds.Count == 0) return new List<string>();
        var presignResult = await _userResourceService.GetPresignedResourcesAsync(userId, resourceIds, ct);
        if (presignResult.IsFailure) return new List<string>();
        return presignResult.Value
            .Where(r => !string.IsNullOrWhiteSpace(r.PresignedUrl))
            .Select(r => r.PresignedUrl)
            .ToList();
    }

    private static string BuildImproveCaptionUserText(
        string originalCaption,
        string? userInstruction,
        string ragAnswer,
        string style)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Requested post STYLE: {style}");
        sb.AppendLine();
        sb.AppendLine("=== Current caption ===");
        sb.AppendLine(string.IsNullOrWhiteSpace(originalCaption) ? "(empty)" : originalCaption);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(userInstruction))
        {
            sb.AppendLine("=== User improvement instruction ===");
            sb.AppendLine(userInstruction);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(ragAnswer))
        {
            sb.AppendLine("=== RAG voice + formula context ===");
            sb.AppendLine(ragAnswer);
            sb.AppendLine();
        }
        sb.AppendLine("Now write the IMPROVED caption (plain text, no Markdown).");
        return sb.ToString();
    }

    private static string BuildImageBriefUserText(
        string captionForImage,
        string? userInstruction,
        string styleKnowledge,
        string style)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Requested post STYLE: {style}");
        sb.AppendLine();
        sb.AppendLine("=== Caption (the new image must illustrate this) ===");
        sb.AppendLine(string.IsNullOrWhiteSpace(captionForImage) ? "(empty)" : captionForImage);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(userInstruction))
        {
            sb.AppendLine("=== User improvement instruction ===");
            sb.AppendLine(userInstruction);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(styleKnowledge))
        {
            sb.AppendLine($"=== Style-knowledge: {style} ===");
            sb.AppendLine(styleKnowledge);
            sb.AppendLine();
        }
        sb.AppendLine("Author the brief (JSON only).");
        return sb.ToString();
    }

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
                    TopK: 6,
                    OnlyNeedContext: true),
                cancellationToken);
            return resp?.Answer ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ImprovePost: style-knowledge fetch failed for style={Style} — proceeding with empty knowledge",
                style);
            return string.Empty;
        }
    }

    private static ImageBrief ParseImageBrief(string raw)
    {
        // Strip ```json fences if the model emitted them despite instructions.
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```"))
        {
            var newline = cleaned.IndexOf('\n');
            if (newline > 0) cleaned = cleaned[(newline + 1)..];
            var fenceEnd = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd > 0) cleaned = cleaned[..fenceEnd];
            cleaned = cleaned.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;
            var prompt = root.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? string.Empty
                : string.Empty;
            var aspect = root.TryGetProperty("aspect_ratio", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() ?? "1:1"
                : "1:1";
            var notes = root.TryGetProperty("style_notes", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                // Fall back to the raw text — better than nothing.
                return new ImageBrief(cleaned, "1:1", null);
            }
            return new ImageBrief(prompt, aspect, notes);
        }
        catch (JsonException)
        {
            return new ImageBrief(cleaned, "1:1", null);
        }
    }

    private sealed record ImageBrief(string Prompt, string AspectRatio, string? StyleNotes);

    private static string SerializeReferences(IReadOnlyList<Application.Recommendations.Queries.RecommendationReference> refs)
    {
        if (refs.Count == 0) return "[]";
        try
        {
            return JsonSerializer.Serialize(refs);
        }
        catch
        {
            return "[]";
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    /// <summary>
    /// Localized literal for the style-design knowledge query. Same table as the
    /// draft-post consumer's helper — kept duplicated so each consumer is
    /// self-contained without a shared utility class. If you add a language here,
    /// add it to DraftPostGenerationConsumer.LocalizedStyleDesignLiteral too.
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
