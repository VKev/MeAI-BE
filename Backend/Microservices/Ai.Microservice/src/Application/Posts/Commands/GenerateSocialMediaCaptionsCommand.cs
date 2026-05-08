using System.Text;
using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Configs;
using Application.Abstractions.Rag;
using Application.Abstractions.Resources;
using Application.Billing;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record GenerateSocialMediaCaptionsCommand(
    Guid UserId,
    SocialMediaCaptionPostInput SocialMedia,
    string? Language,
    string? Instruction,
    int? MaxTokens,
    string? Style,
    bool WebSearch) : IRequest<Result<GenerateSocialMediaCaptionsResponse>>;

public sealed record SocialMediaCaptionPostInput(
    Guid PostId,
    string SocialMediaType,
    IReadOnlyList<Guid> ResourceIds);

public sealed class GenerateSocialMediaCaptionsCommandHandler
    : IRequestHandler<GenerateSocialMediaCaptionsCommand, Result<GenerateSocialMediaCaptionsResponse>>
{
    private const int DefaultCaptionCount = 3;
    private const int MaxCaptionCount = 6;
    private const int DefaultMaxOutputTokens = 700;
    public const int BackendMaxOutputTokens = 1000;

    private const string CaptionModel = "openai/gpt-4o";
    private const string KnowledgeDocumentPrefix = "knowledge:";

    private readonly IPostRepository _postRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly IUserConfigService _userConfigService;
    private readonly IRagClient _ragClient;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly ICoinPricingService _pricingService;
    private readonly IBillingClient _billingClient;
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;

    public GenerateSocialMediaCaptionsCommandHandler(
        IPostRepository postRepository,
        IUserResourceService userResourceService,
        IUserConfigService userConfigService,
        IRagClient ragClient,
        IMultimodalLlmClient multimodalLlm,
        ICoinPricingService pricingService,
        IBillingClient billingClient,
        IAiSpendRecordRepository aiSpendRecordRepository)
    {
        _postRepository = postRepository;
        _userResourceService = userResourceService;
        _userConfigService = userConfigService;
        _ragClient = ragClient;
        _multimodalLlm = multimodalLlm;
        _pricingService = pricingService;
        _billingClient = billingClient;
        _aiSpendRecordRepository = aiSpendRecordRepository;
    }

    public async Task<Result<GenerateSocialMediaCaptionsResponse>> Handle(
        GenerateSocialMediaCaptionsCommand request,
        CancellationToken cancellationToken)
    {
        var socialMediaResult = NormalizePlatform(request.SocialMedia);
        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(socialMediaResult.Error);
        }

        var maxTokensResult = ResolveMaxOutputTokens(request.MaxTokens);
        if (maxTokensResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(maxTokensResult.Error);
        }

        var styleResult = ResolveStyle(request.Style);
        if (styleResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(styleResult.Error);
        }

        var socialMedia = socialMediaResult.Value;
        var style = styleResult.Value;
        var post = await _postRepository.GetByIdAsync(socialMedia.PostId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(PostErrors.Unauthorized);
        }

        var resourcesResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            socialMedia.ResourceIds,
            cancellationToken);

        if (resourcesResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(resourcesResult.Error);
        }

        var resourcesById = resourcesResult.Value.ToDictionary(resource => resource.ResourceId);
        if (!TryResolveResources(resourcesById, socialMedia.ResourceIds, out var orderedResources))
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(
                new Error("Resource.NotFound", "One or more resources were not found."));
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var captionCount = Math.Clamp(
            activeConfig?.NumberOfVariances ?? DefaultCaptionCount,
            1,
            MaxCaptionCount);
        var languageHint = ResolveLanguageHint(request.Language);
        var postType = GeminiDraftPostHelper.NormalizePostType(post.Content?.PostType);

        var knowledgeResult = await SearchKnowledgeAsync(
            post,
            socialMedia,
            languageHint,
            request.Instruction,
            postType,
            style,
            cancellationToken);
        if (knowledgeResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(knowledgeResult.Error);
        }

        var quoteResult = await _pricingService.GetCostAsync(
            CoinActionTypes.CaptionGeneration,
            CaptionModel,
            variant: null,
            quantity: 1,
            cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(quoteResult.Error);
        }

        var batchReferenceId = Guid.CreateVersion7().ToString();
        var debitResult = await _billingClient.DebitAsync(
            request.UserId,
            quoteResult.Value.TotalCoins,
            CoinDebitReasons.CaptionGenerationDebit,
            CoinReferenceTypes.CaptionBatch,
            batchReferenceId,
            cancellationToken);
        if (debitResult.IsFailure)
        {
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(debitResult.Error);
        }

        var spendRecord = new AiSpendRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = ResolveWorkspaceId(post),
            Provider = AiSpendProviders.OpenRouter,
            ActionType = CoinActionTypes.CaptionGeneration,
            Model = CaptionModel,
            Variant = null,
            Unit = quoteResult.Value.Unit,
            Quantity = quoteResult.Value.Quantity,
            UnitCostCoins = quoteResult.Value.UnitCostCoins,
            TotalCoins = quoteResult.Value.TotalCoins,
            ReferenceType = CoinReferenceTypes.CaptionBatch,
            ReferenceId = batchReferenceId,
            Status = AiSpendStatuses.Debited,
            CreatedAt = DateTime.UtcNow
        };

        await _aiSpendRecordRepository.AddAsync(spendRecord, cancellationToken);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);

        var captionsResult = await GenerateCaptionsAsync(
            post,
            socialMedia,
            orderedResources,
            captionCount,
            languageHint,
            request.Instruction,
            knowledgeResult.Value,
            maxTokensResult.Value,
            style,
            request.WebSearch,
            cancellationToken);

        if (captionsResult.IsFailure)
        {
            await RefundCaptionBatchAsync(
                request.UserId,
                quoteResult.Value.TotalCoins,
                batchReferenceId,
                spendRecord,
                cancellationToken);
            return Result.Failure<GenerateSocialMediaCaptionsResponse>(captionsResult.Error);
        }

        return Result.Success(new GenerateSocialMediaCaptionsResponse(
        [
            new SocialMediaCaptionsByPostResponse(
                socialMedia.PostId,
                socialMedia.SocialMediaType,
                socialMedia.ResourceIds,
                captionsResult.Value)
        ]));
    }

    private async Task<Result<RagKnowledgeContext>> SearchKnowledgeAsync(
        Post post,
        SocialMediaCaptionPostInput socialMedia,
        string? languageHint,
        string? instruction,
        string postType,
        string style,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ragClient.WaitForRagReadyAsync(cancellationToken);
            var generalResponse = await _ragClient.QueryAsync(
                new RagQueryRequest(
                    BuildKnowledgeQuery(post, socialMedia, languageHint, instruction, postType, style),
                    DocumentIdPrefix: KnowledgeDocumentPrefix,
                    Mode: "hybrid",
                    TopK: 8,
                    OnlyNeedContext: true),
                cancellationToken);

            var styleResponse = await _ragClient.QueryAsync(
                new RagQueryRequest(
                    LocalizedStyleDesignLiteral(languageHint, style),
                    DocumentIdPrefix: $"knowledge:image-design-{style}:",
                    Mode: "naive",
                    TopK: 8,
                    OnlyNeedContext: true),
                cancellationToken);

            return Result.Success(new RagKnowledgeContext(
                CombineKnowledgeContext(generalResponse.Answer, style, styleResponse.Answer),
                (generalResponse.MatchedDocumentIds ?? [])
                    .Concat(styleResponse.MatchedDocumentIds ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure<RagKnowledgeContext>(
                new Error("Rag.QueryFailed", "Unable to search knowledge for caption generation."));
        }
    }

    private async Task<Result<IReadOnlyList<GeneratedCaptionResponse>>> GenerateCaptionsAsync(
        Post post,
        SocialMediaCaptionPostInput socialMedia,
        IReadOnlyList<UserResourcePresignResult> orderedResources,
        int captionCount,
        string? languageHint,
        string? instruction,
        RagKnowledgeContext knowledge,
        int maxOutputTokens,
        string style,
        bool webSearchEnabled,
        CancellationToken cancellationToken)
    {
        MultimodalAnswerResult answer;
        try
        {
            answer = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    BuildSystemPrompt(captionCount, webSearchEnabled),
                    BuildCaptionUserText(
                        post,
                        socialMedia,
                        orderedResources,
                        captionCount,
                        languageHint,
                        instruction,
                        knowledge,
                        maxOutputTokens,
                        style,
                        webSearchEnabled),
                    BuildReferenceImageUrls(orderedResources),
                    ModelOverride: CaptionModel,
                    MaxOutputTokens: maxOutputTokens,
                    WebSearchEnabled: webSearchEnabled),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure<IReadOnlyList<GeneratedCaptionResponse>>(
                new Error("Caption.GenerationFailed", "OpenRouter caption generation failed."));
        }

        return ParseOpenRouterCaptions(answer.Answer, captionCount);
    }

    private async Task RefundCaptionBatchAsync(
        Guid userId,
        decimal totalCoins,
        string batchReferenceId,
        AiSpendRecord spendRecord,
        CancellationToken cancellationToken)
    {
        var refundResult = await _billingClient.RefundAsync(
            userId,
            totalCoins,
            CoinDebitReasons.CaptionGenerationRefund,
            CoinReferenceTypes.CaptionBatch,
            batchReferenceId,
            cancellationToken);

        if (refundResult.IsFailure)
        {
            return;
        }

        spendRecord.Status = AiSpendStatuses.Refunded;
        spendRecord.UpdatedAt = DateTime.UtcNow;
        _aiSpendRecordRepository.Update(spendRecord);
        await _aiSpendRecordRepository.SaveChangesAsync(cancellationToken);
    }

    private static Result<SocialMediaCaptionPostInput> NormalizePlatform(
        SocialMediaCaptionPostInput socialMedia)
    {
        if (socialMedia.PostId == Guid.Empty)
        {
            return Result.Failure<SocialMediaCaptionPostInput>(
                new Error("Post.InvalidRequest", "A valid postId is required."));
        }

        var normalizedTypeResult = NormalizePlatformType(socialMedia.SocialMediaType);
        if (normalizedTypeResult.IsFailure)
        {
            return Result.Failure<SocialMediaCaptionPostInput>(normalizedTypeResult.Error);
        }

        var resourceIds = socialMedia.ResourceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (resourceIds.Count == 0)
        {
            return Result.Failure<SocialMediaCaptionPostInput>(
                new Error("Resource.Missing", "At least one resource is required."));
        }

        return Result.Success(new SocialMediaCaptionPostInput(
            socialMedia.PostId,
            normalizedTypeResult.Value,
            resourceIds));
    }

    private static Result<int> ResolveMaxOutputTokens(int? requestedMaxTokens)
    {
        if (requestedMaxTokens is null)
        {
            return Result.Success(DefaultMaxOutputTokens);
        }

        if (requestedMaxTokens <= 0)
        {
            return Result.Failure<int>(
                new Error("Caption.InvalidMaxTokens", "maxTokens must be a positive integer."));
        }

        if (requestedMaxTokens > BackendMaxOutputTokens)
        {
            return Result.Failure<int>(
                new Error(
                    "Caption.MaxTokensExceeded",
                    $"maxTokens must be less than or equal to {BackendMaxOutputTokens}."));
        }

        return Result.Success(requestedMaxTokens.Value);
    }

    private static Result<string> ResolveStyle(string? rawStyle)
    {
        if (string.Equals(rawStyle?.Trim(), "marketting", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(DraftPostStyles.Marketing);
        }

        if (DraftPostStyles.TryValidate(rawStyle, out var style))
        {
            return Result.Success(style);
        }

        return Result.Failure<string>(
            new Error(
                "Caption.InvalidStyle",
                "style must be one of: creative, branded, marketing."));
    }

    private static Result<string> NormalizePlatformType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return Result.Failure<string>(
                new Error("SocialMedia.InvalidType", "platform is required."));
        }

        var normalized = rawType.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "facebook" or "fb" => Result.Success("facebook"),
            "tiktok" => Result.Success("tiktok"),
            "instagram" or "ig" => Result.Success("ig"),
            "threads" => Result.Success("threads"),
            _ => Result.Failure<string>(
                new Error("SocialMedia.UnsupportedPlatform", "Only Facebook, Instagram, TikTok, and Threads are supported."))
        };
    }

    private static string? ResolveLanguageHint(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "vi" or "vn" or "vietnamese" => "Vietnamese",
            "en" or "english" => "English",
            _ => language.Trim()
        };
    }

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private static bool TryResolveResources(
        IReadOnlyDictionary<Guid, UserResourcePresignResult> resourcesById,
        IReadOnlyList<Guid> resourceIds,
        out List<UserResourcePresignResult> orderedResources)
    {
        orderedResources = new List<UserResourcePresignResult>(resourceIds.Count);
        foreach (var resourceId in resourceIds)
        {
            if (!resourcesById.TryGetValue(resourceId, out var resource))
            {
                return false;
            }

            orderedResources.Add(resource);
        }

        return true;
    }

    private static IReadOnlyList<string> BuildResourceHints(Post post)
    {
        var hints = new List<string>();

        if (!string.IsNullOrWhiteSpace(post.Title))
        {
            hints.Add(post.Title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(post.Content?.Content))
        {
            var content = post.Content.Content.Trim();
            if (content.Length > 140)
            {
                content = content[..140].TrimEnd();
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                hints.Add(content);
            }
        }

        return hints;
    }

    private static IReadOnlyList<string> BuildReferenceImageUrls(
        IReadOnlyList<UserResourcePresignResult> resources)
    {
        return resources
            .Where(IsImageResource)
            .Select(resource => resource.PresignedUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsImageResource(UserResourcePresignResult resource)
    {
        return (!string.IsNullOrWhiteSpace(resource.ContentType) &&
                resource.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(resource.ResourceType) &&
                resource.ResourceType.Contains("image", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildKnowledgeQuery(
        Post post,
        SocialMediaCaptionPostInput socialMedia,
        string? languageHint,
        string? instruction,
        string postType,
        string style)
    {
        var builder = new StringBuilder();
        builder.Append("caption generation guidance");
        builder.Append(" platform ").Append(DisplayPlatform(socialMedia.SocialMediaType));
        builder.Append(" post type ").Append(postType);
        builder.Append(" style ").Append(style);

        if (!string.IsNullOrWhiteSpace(languageHint))
        {
            builder.Append(" language ").Append(languageHint);
        }

        if (!string.IsNullOrWhiteSpace(post.Title))
        {
            builder.Append(" title ").Append(post.Title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(post.Content?.Content))
        {
            builder.Append(" content ").Append(Truncate(post.Content.Content.Trim(), 300));
        }

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.Append(" instruction ").Append(Truncate(instruction.Trim(), 300));
        }

        return builder.ToString();
    }

    private static string BuildSystemPrompt(int captionCount, bool webSearchEnabled)
    {
        var webSearchInstruction = webSearchEnabled
            ? "Web search is enabled. Use it only for current public context such as recent trends, timely hooks, or current hashtag ideas when it improves the caption. Still prioritize the user's media and RAG knowledge."
            : "Web search is disabled. Do not search the public web.";

        return $$"""
You generate platform-ready social captions for MeAI.
Use only the user's media, post details, and the supplied knowledge context.
{{webSearchInstruction}}
Do not use social-media post RAG data.
Return strict JSON only, with this schema:
{"captions":[{"caption":"...","hashtags":["#..."],"trendingHashtags":["#..."],"callToAction":"..."}]}
Generate exactly {{captionCount}} caption objects when the output token limit allows it.
Keep hashtags out of the caption text; place them in the arrays.
""";
    }

    private static string BuildCaptionUserText(
        Post post,
        SocialMediaCaptionPostInput socialMedia,
        IReadOnlyList<UserResourcePresignResult> resources,
        int captionCount,
        string? languageHint,
        string? instruction,
        RagKnowledgeContext knowledge,
        int maxOutputTokens,
        string style,
        bool webSearchEnabled)
    {
        var platformName = DisplayPlatform(socialMedia.SocialMediaType);
        var postTypeLabel = NormalizePostTypeLabel(post.Content?.PostType, socialMedia.SocialMediaType);
        var toneGuidance = BuildToneGuidance(platformName, postTypeLabel);
        var resourceHints = BuildResourceHints(post);
        var hasImages = resources.Any(IsImageResource);

        var builder = new StringBuilder();
        if (hasImages)
        {
            builder.AppendLine("You are writing social-media captions for the images attached in this message.");
            builder.AppendLine(
                "Before writing, examine every image carefully: subject, setting, colors, activity, mood, text-on-image.");
            builder.AppendLine(
                "Ground each caption in what's actually visible - don't invent details that aren't in the images.");
        }
        else
        {
            builder.AppendLine("You are writing social-media captions. No reference images are attached - rely on the hints below.");
        }

        builder.AppendLine();
        builder.AppendLine($"Target platform: {platformName}");
        builder.AppendLine($"Post format: {postTypeLabel}");
        builder.AppendLine($"Caption style: {style}");
        builder.AppendLine($"Web search: {(webSearchEnabled ? "enabled" : "disabled")}");
        builder.AppendLine($"Language: {languageHint ?? "English"}");
        builder.AppendLine($"Number of captions to produce: {captionCount} (distinct, different hooks/angles each).");
        builder.AppendLine($"Max output tokens: {maxOutputTokens}");
        builder.AppendLine();
        builder.AppendLine(toneGuidance);

        if (resourceHints.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Context hints from the user's earlier draft (use for tone/topic, don't copy verbatim):");
            foreach (var hint in resourceHints)
            {
                builder.AppendLine($"- {hint}");
            }
        }

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.AppendLine();
            builder.AppendLine($"Additional creator instruction: {instruction.Trim()}");
        }

        if (resources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Media resources:");
            foreach (var resource in resources)
            {
                builder.AppendLine(
                    $"- {resource.ResourceId}: type={resource.ResourceType ?? "unknown"}, contentType={resource.ContentType ?? "unknown"}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Knowledge context from RAG knowledge documents only:");
        builder.AppendLine("<knowledge>");
        builder.AppendLine(string.IsNullOrWhiteSpace(knowledge.Context)
            ? "No matching knowledge context was returned."
            : Truncate(knowledge.Context.Trim(), 6000));
        builder.AppendLine("</knowledge>");

        if (knowledge.MatchedDocumentIds.Count > 0)
        {
            builder.AppendLine("Matched knowledge document ids:");
            foreach (var documentId in knowledge.MatchedDocumentIds.Take(8))
            {
                builder.AppendLine($"- {documentId}");
            }
        }

        return builder.ToString();
    }

    private static string CombineKnowledgeContext(
        string? generalKnowledge,
        string style,
        string? styleKnowledge)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(generalKnowledge))
        {
            builder.AppendLine("=== General caption knowledge ===");
            builder.AppendLine(generalKnowledge.Trim());
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(styleKnowledge))
        {
            builder.AppendLine($"=== Style-specific image/copy knowledge for {style} ===");
            builder.AppendLine(styleKnowledge.Trim());
        }

        return builder.ToString().Trim();
    }

    private static string NormalizePostTypeLabel(string? postType, string platform)
    {
        var normalized = GeminiDraftPostHelper.NormalizePostType(postType);
        if (normalized is "reel" or "reels")
        {
            return platform.Equals("tiktok", StringComparison.OrdinalIgnoreCase)
                ? "short-form video (TikTok style)"
                : "reel (short-form vertical video)";
        }

        if (normalized is "video")
        {
            return "short-form video";
        }

        return platform.Equals("tiktok", StringComparison.OrdinalIgnoreCase)
            ? "short-form video (TikTok style)"
            : "feed post";
    }

    private static string BuildToneGuidance(string platformName, string postTypeLabel)
    {
        var isReelLike = postTypeLabel.Contains("video", StringComparison.OrdinalIgnoreCase)
                         || postTypeLabel.Contains("reel", StringComparison.OrdinalIgnoreCase);

        if (isReelLike)
        {
            return
                $"Tone guidance: {platformName} short-form video captions should lead with a 1-line hook (<= 10 words) " +
                "that stops scroll, use casual conversational language, and close with an invitation to watch/save/share. " +
                "Keep total length under 150 characters. 3-5 hashtags max, mix niche + trending.";
        }

        return platformName switch
        {
            "Instagram" =>
                "Tone guidance: Instagram feed posts can run 150-220 characters - warm, visual, emoji-friendly. " +
                "Start with a strong first line (the part that shows before 'more'). Include 5-10 relevant hashtags.",
            "Facebook" =>
                "Tone guidance: Facebook feed posts can be 150-300 characters - conversational, slightly longer, " +
                "share-friendly. Avoid heavy hashtag stacking (2-4 is plenty).",
            "Threads" =>
                "Tone guidance: Threads is short + text-first (<= 500 chars). Punchy, meme-aware, 0-3 hashtags.",
            _ =>
                "Tone guidance: match platform best practices, keep captions concise and scroll-stopping."
        };
    }

    private static Result<IReadOnlyList<GeneratedCaptionResponse>> ParseOpenRouterCaptions(
        string? rawAnswer,
        int maxCount)
    {
        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            return Result.Failure<IReadOnlyList<GeneratedCaptionResponse>>(
                new Error("Caption.InvalidResponse", "Caption model returned an empty response."));
        }

        var json = ExtractJsonPayload(rawAnswer);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<IReadOnlyList<GeneratedCaptionResponse>>(
                new Error("Caption.InvalidResponse", "Caption model did not return JSON."));
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var captionElements = ResolveCaptionElements(document.RootElement);
            var captions = new List<GeneratedCaptionResponse>();

            foreach (var element in captionElements.Take(maxCount))
            {
                var caption = ReadString(element, "caption", "text", "content");
                if (string.IsNullOrWhiteSpace(caption))
                {
                    continue;
                }

                captions.Add(new GeneratedCaptionResponse(
                    caption.Trim(),
                    ReadHashtagArray(element, "hashtags", "hashTags"),
                    ReadHashtagArray(element, "trendingHashtags", "trending_hashtags", "trending"),
                    NormalizeOptionalString(ReadString(element, "callToAction", "call_to_action", "cta"))));
            }

            if (captions.Count == 0)
            {
                return Result.Failure<IReadOnlyList<GeneratedCaptionResponse>>(
                    new Error("Caption.InvalidResponse", "Caption model returned no usable captions."));
            }

            return Result.Success<IReadOnlyList<GeneratedCaptionResponse>>(captions);
        }
        catch (JsonException)
        {
            return Result.Failure<IReadOnlyList<GeneratedCaptionResponse>>(
                new Error("Caption.InvalidResponse", "Caption model returned invalid JSON."));
        }
    }

    private static IEnumerable<JsonElement> ResolveCaptionElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            TryGetProperty(root, "captions", out var captions) &&
            captions.ValueKind == JsonValueKind.Array)
        {
            return captions.EnumerateArray().ToList();
        }

        return root.ValueKind == JsonValueKind.Object
            ? [root]
            : [];
    }

    private static string? ExtractJsonPayload(string rawAnswer)
    {
        var trimmed = rawAnswer.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

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

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadHashtagArray(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            var tags = new List<string>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        tags.AddRange(SplitHashtags(item.GetString()));
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                tags.AddRange(SplitHashtags(value.GetString()));
            }

            return tags
                .Select(NormalizeHashtag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<string>();
    }

    private static IEnumerable<string> SplitHashtags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(
            [',', '\n', '\r', '\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeHashtag(string raw)
    {
        var normalized = raw.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.TrimStart('#');
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $"#{normalized}";
    }

    private static string? NormalizeOptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Guid? ResolveWorkspaceId(Post post)
    {
        return post.WorkspaceId.HasValue && post.WorkspaceId.Value != Guid.Empty
            ? post.WorkspaceId.Value
            : null;
    }

    private static string DisplayPlatform(string socialMediaType)
    {
        return socialMediaType switch
        {
            "ig" => "Instagram",
            "facebook" => "Facebook",
            "tiktok" => "TikTok",
            "threads" => "Threads",
            _ => socialMediaType
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd();
    }

    private sealed record RagKnowledgeContext(
        string? Context,
        IReadOnlyList<string> MatchedDocumentIds);

    private static string LocalizedStyleDesignLiteral(string? language, string style)
    {
        var normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedLanguage switch
        {
            "vietnamese" or "vi" or "vn" =>
                $"quy tac thiet ke hinh anh cho phong cach {style} tren mang xa hoi",
            "japanese" or "ja" =>
                $"social media post {style} style image design rules",
            "korean" or "ko" =>
                $"social media post {style} style image design rules",
            "thai" or "th" =>
                $"social media post {style} style image design rules",
            "chinese" or "zh" =>
                $"social media post {style} style image design rules",
            "spanish" or "es" =>
                $"reglas de diseno de imagen para estilo {style} en redes sociales",
            "portuguese" or "pt" =>
                $"regras de design de imagem para estilo {style} em redes sociais",
            "french" or "fr" =>
                $"regles de conception d'image pour style {style} sur les reseaux sociaux",
            "german" or "de" =>
                $"image design rules for {style} style social media post",
            "indonesian" or "id" =>
                $"aturan desain gambar untuk gaya {style} di media sosial",
            _ =>
                $"image design rules for {style} style social media post",
        };
    }
}
