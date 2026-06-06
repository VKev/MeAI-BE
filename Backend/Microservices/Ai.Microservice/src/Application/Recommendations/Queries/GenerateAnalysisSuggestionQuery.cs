using System.Text;
using Application.Abstractions.Rag;
using Application.Posts;
using Application.Posts.Models;
using Application.Posts.Queries;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;

namespace Application.Recommendations.Queries;

public sealed record GenerateAnalysisSuggestionQuery(
    Guid UserId,
    Guid SocialMediaId,
    Guid CorrelationId,
    AnalysisSuggestionRequest Request) : IRequest<Result<AnalysisSuggestionResponse>>;

public sealed class GenerateAnalysisSuggestionQueryHandler
    : IRequestHandler<GenerateAnalysisSuggestionQuery, Result<AnalysisSuggestionResponse>>
{
#pragma warning disable CS0169
    private readonly RecommendPost? _domainDependency;
#pragma warning restore CS0169

    private const int DefaultPostLimit = 8;
    private const int MaxPostLimit = 8;
    private const int DefaultTopK = 8;
    private const int MaxTopK = 20;
    private const int DefaultMaxRagPosts = 50;
    private const int MaxRagPosts = 200;
    private const int MaxImagesToLlm = 4;

    private const string SystemPrompt =
        "You are a senior social media analyst and content strategist. " +
        "Analyze the connected account using the supplied account analytics, recent post metrics, " +
        "retrieved RAG context from the account's posts, and marketing knowledge. " +
        "Return strict JSON only. Do not wrap it in Markdown fences. Do not invent metrics or claim access to data not provided. " +
        "Always write the entire answer in English, including every title, point, next-post prompt, why explanation, and action. " +
        "Do not switch to the account language even when the profile, posts, or RAG context use another language. " +
        "The JSON shape must be: {\"summary\":\"one sentence\",\"cards\":[{\"title\":\"Overall diagnosis\",\"tone\":\"diagnosis\",\"points\":[\"...\"]},{\"title\":\"What is working\",\"tone\":\"positive\",\"points\":[\"...\"]},{\"title\":\"What is wrong with current posts\",\"tone\":\"warning\",\"points\":[\"...\"]},{\"title\":\"Grammar and copy fixes\",\"tone\":\"copy\",\"points\":[\"...\"]},{\"title\":\"Engagement fixes\",\"tone\":\"engagement\",\"points\":[\"...\"]},{\"title\":\"What content to create next\",\"tone\":\"ideas\",\"points\":[\"...\"]}],\"nextPostIdeas\":[{\"title\":\"...\",\"prompt\":\"...\",\"why\":\"...\"}],\"immediateAction\":\"one concrete next action\"}. " +
        "Cover these sections: Overall diagnosis, What is working, What is wrong with current posts, " +
        "Grammar and copy fixes, Engagement fixes, What content to create next, and One immediate next-post idea. " +
        "Each card should have 2-4 concise points. nextPostIdeas should contain 2-4 ideas, including at least one bold creative idea when useful. " +
        "Be concrete: reference specific posts, metrics, captions, visual patterns, and audience signals when available. " +
        "If the data is sparse, say exactly what is missing and still give a useful next step.";

    private readonly IMediator _mediator;
    private readonly IRagClient _ragClient;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<GenerateAnalysisSuggestionQueryHandler> _logger;

    public GenerateAnalysisSuggestionQueryHandler(
        IMediator mediator,
        IRagClient ragClient,
        IMultimodalLlmClient multimodalLlm,
        IPublishEndpoint publishEndpoint,
        ILogger<GenerateAnalysisSuggestionQueryHandler> logger)
    {
        _mediator = mediator;
        _ragClient = ragClient;
        _multimodalLlm = multimodalLlm;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<AnalysisSuggestionResponse>> Handle(
        GenerateAnalysisSuggestionQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Request.From.HasValue &&
            request.Request.To.HasValue &&
            request.Request.From.Value > request.Request.To.Value)
        {
            return Result.Failure<AnalysisSuggestionResponse>(
                new Error("AnalysisSuggest.InvalidPeriod", "from must be earlier than or equal to to."));
        }

        var postLimit = Math.Clamp(request.Request.PostLimit ?? DefaultPostLimit, 1, MaxPostLimit);
        var topK = Math.Clamp(request.Request.TopK ?? DefaultTopK, 1, MaxTopK);
        var maxRagPosts = Math.Clamp(request.Request.MaxRagPosts ?? DefaultMaxRagPosts, 1, MaxRagPosts);

        await PublishThinkingAsync(
            request,
            "account_summary_reading",
            "AI is reading account performance",
            "Collecting account metrics and recent posts before writing the recommendation.",
            new { postLimit },
            cancellationToken);

        var summaryResult = await _mediator.Send(
            new GetSocialMediaDashboardSummaryQuery(
                request.UserId,
                request.SocialMediaId,
                postLimit),
            cancellationToken);

        if (summaryResult.IsFailure)
        {
            return Result.Failure<AnalysisSuggestionResponse>(summaryResult.Error);
        }

        var summary = summaryResult.Value;
        var platform = NormalizePlatform(summary.Platform);
        var prefix = $"{platform}:{request.SocialMediaId:N}:";

        await PublishThinkingAsync(
            request,
            "account_summary_reading",
            "AI read account performance",
            "Recent posts and metrics are ready for analysis.",
            new
            {
                platform,
                fetchedPostCount = summary.FetchedPostCount,
                postCount = summary.Posts.Count
            },
            cancellationToken,
            phaseStatus: "completed");

        if (request.Request.RefreshIndex != false)
        {
            await PublishThinkingAsync(
                request,
                "account_posts_indexing",
                "AI is updating account memory",
                "Indexing recent account posts into RAG so the analysis can use current context.",
                new { maxRagPosts },
                cancellationToken);

            var indexResult = await _mediator.Send(
                new IndexSocialAccountPostsCommand(
                    request.UserId,
                    request.SocialMediaId,
                    maxRagPosts),
                cancellationToken);

            if (indexResult.IsFailure)
            {
                return Result.Failure<AnalysisSuggestionResponse>(indexResult.Error);
            }

            platform = NormalizePlatform(indexResult.Value.Platform);
            prefix = indexResult.Value.DocumentIdPrefix;

            await PublishThinkingAsync(
                request,
                "account_posts_indexing",
                "AI updated account memory",
                "RAG memory is ready for this account's latest posts.",
                new
                {
                    platform,
                    documentIdPrefix = prefix
                },
                cancellationToken,
                phaseStatus: "completed");
        }

        try
        {
            await PublishThinkingAsync(
                request,
                "rag_ready_wait",
                "AI is checking RAG readiness",
                "Waiting for knowledge and account memory services to be available.",
                null,
                cancellationToken);

            await _ragClient.WaitForRagReadyAsync(cancellationToken);

            await PublishThinkingAsync(
                request,
                "rag_ready_wait",
                "RAG is ready",
                "Knowledge and account memory are available.",
                null,
                cancellationToken,
                phaseStatus: "completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG readiness check failed for socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<AnalysisSuggestionResponse>(
                new Error("AnalysisSuggest.RagUnavailable", $"RAG service is not ready: {ex.Message}"));
        }

        var posts = FilterPosts(summary.Posts, request.Request.From, request.Request.To)
            .Take(postLimit)
            .ToList();

        var aggregatedStats = BuildAggregatedStats(posts);
        var aggregateAnalysis = SocialPlatformPostAnalysisFactory.Create(aggregatedStats);
        var postReferences = posts
            .Select(item => new AnalysisSuggestionPostReference(
                PlatformPostId: item.Post.PlatformPostId,
                Title: item.Post.Title,
                Text: item.Post.Text,
                MediaType: item.Post.MediaType,
                PublishedAt: item.Post.PublishedAt,
                Permalink: item.Post.Permalink ?? item.Post.ShareUrl,
                Stats: item.Post.Stats,
                Analysis: item.Analysis))
            .ToList();

        var ragQuery = BuildRagQuery(platform, request.Request.From, request.Request.To, request.Request.Instruction);
        RagMultimodalQueryResponse rag;
        try
        {
            await PublishThinkingAsync(
                request,
                "account_rag_query",
                "AI is searching account memory",
                "Finding relevant past posts, visuals, video frames, and post context for this account.",
                new
                {
                    query = ragQuery,
                    topK,
                    platform
                },
                cancellationToken);

            rag = await _ragClient.MultimodalQueryAsync(
                new RagMultimodalQueryRequest(
                    Query: ragQuery,
                    DocumentIdPrefix: prefix,
                    TopK: topK,
                    Modes: new[] { "text", "visual", "video" },
                    Platform: platform,
                    SocialMediaId: request.SocialMediaId.ToString("N")),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG analysis query failed for socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<AnalysisSuggestionResponse>(
                new Error("AnalysisSuggest.RagQueryFailed", $"Unable to read account posts from RAG: {ex.Message}"));
        }

        var profileTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: "page profile account identity about category website email phone location bio",
                DocumentIdPrefix: $"{prefix}profile",
                Mode: "naive",
                TopK: 1,
                OnlyNeedContext: true),
            cancellationToken);

        var formulaTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: $"content formulas and copywriting frameworks for {platform} posts",
                DocumentIdPrefix: "knowledge:content-formulas:",
                Mode: "hybrid",
                TopK: 3,
                OnlyNeedContext: true),
            cancellationToken);

        var engagementTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: $"engagement triggers and platform algorithm signals for {platform}",
                DocumentIdPrefix: "knowledge:engagement-triggers:",
                Mode: "hybrid",
                TopK: 3,
                OnlyNeedContext: true),
            cancellationToken);

        var algorithmTask = _ragClient.QueryAsync(
            new RagQueryRequest(
                Query: $"{platform} algorithm signals content performance engagement",
                DocumentIdPrefix: "knowledge:platform-algorithm-signals:",
                Mode: "hybrid",
                TopK: 3,
                OnlyNeedContext: true),
            cancellationToken);

        await PublishThinkingAsync(
            request,
            "strategy_knowledge_lookup",
            "AI is checking strategy knowledge",
            "Reading page profile, content formulas, engagement triggers, and platform algorithm notes.",
            null,
            cancellationToken);

        var pageProfile = await SafeQueryAsync(profileTask, "page profile", request.SocialMediaId);
        var formulaKnowledge = await SafeQueryAsync(formulaTask, "content formulas", request.SocialMediaId);
        var engagementKnowledge = await SafeQueryAsync(engagementTask, "engagement triggers", request.SocialMediaId);
        var algorithmKnowledge = await SafeQueryAsync(algorithmTask, "algorithm signals", request.SocialMediaId);

        var references = BuildRagReferences(rag);
        var retrievalErrors = BuildRetrievalErrors(rag);

        await PublishThinkingAsync(
            request,
            "account_rag_query",
            "AI found account references",
            "Relevant account memories and visual references were retrieved.",
            new
            {
                referenceCount = references.Count,
                textContextLength = rag.Text?.Context?.Length ?? 0,
                visualCount = rag.Visual?.Count ?? 0,
                videoCount = rag.Video?.Count ?? 0,
                retrievalErrors = retrievalErrors.Count
            },
            cancellationToken,
            phaseStatus: retrievalErrors.Count > 0 ? "warning" : "completed");

        await PublishThinkingAsync(
            request,
            "strategy_knowledge_lookup",
            "AI read strategy knowledge",
            "Formula, engagement, and platform notes are ready for the final suggestion.",
            new
            {
                hasProfile = !string.IsNullOrWhiteSpace(pageProfile?.Answer),
                hasFormulas = !string.IsNullOrWhiteSpace(formulaKnowledge?.Answer),
                hasEngagementKnowledge = !string.IsNullOrWhiteSpace(engagementKnowledge?.Answer),
                hasAlgorithmKnowledge = !string.IsNullOrWhiteSpace(algorithmKnowledge?.Answer)
            },
            cancellationToken,
            phaseStatus: "completed");

        var imageRefsForLlm = references
            .Select(reference => reference.ImageUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxImagesToLlm)
            .ToList();

        var userText = BuildUserText(
            platform,
            request.Request,
            summary,
            posts,
            aggregatedStats,
            aggregateAnalysis,
            rag,
            references,
            pageProfile,
            formulaKnowledge,
            engagementKnowledge,
            algorithmKnowledge);

        _logger.LogInformation(
            "LLM[analysis-suggest] INPUT for socialMediaId={SocialMediaId} posts={PostCount} ragText={RagTextLen} images={ImageCount}",
            request.SocialMediaId,
            posts.Count,
            rag.Text?.Context?.Length ?? 0,
            imageRefsForLlm.Count);

        MultimodalAnswerResult llmResult;
        try
        {
            await PublishThinkingAsync(
                request,
                "analysis_cards_generation",
                "AI is writing analysis cards",
                "Generating structured diagnosis, fixes, and next-content ideas for the account.",
                new
                {
                    analyzedPostCount = posts.Count,
                    imageReferenceCount = imageRefsForLlm.Count
                },
                cancellationToken);

            llmResult = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: SystemPrompt,
                    UserText: userText,
                    ReferenceImageUrls: imageRefsForLlm,
                    MaxOutputTokens: 2200,
                    WebSearchEnabled: true),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis suggestion LLM call failed for socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<AnalysisSuggestionResponse>(
                new Error("AnalysisSuggest.LlmFailed", $"Suggestion generation failed: {ex.Message}"));
        }

        await PublishThinkingAsync(
            request,
            "analysis_cards_generation",
            "AI finished analysis cards",
            "Structured account recommendations are ready.",
            new
            {
                outputLength = llmResult.Answer.Length,
                webSourceCount = llmResult.Sources.Count
            },
            cancellationToken,
            phaseStatus: "completed");

        return Result.Success(new AnalysisSuggestionResponse(
            SocialMediaId: request.SocialMediaId,
            Platform: platform,
            Suggestion: llmResult.Answer,
            DocumentIdPrefix: prefix,
            GeneratedAt: DateTimeOffset.UtcNow,
            From: request.Request.From,
            To: request.Request.To,
            AnalyzedPostCount: posts.Count,
            AggregatedStats: aggregatedStats,
            AggregateAnalysis: aggregateAnalysis,
            AccountInsights: summary.AccountInsights,
            Posts: postReferences,
            References: references,
            WebSources: llmResult.Sources.Count > 0 ? llmResult.Sources : null,
            RetrievalErrors: retrievalErrors.Count > 0 ? retrievalErrors : null));
    }

    private async Task PublishThinkingAsync(
        GenerateAnalysisSuggestionQuery request,
        string action,
        string title,
        string message,
        object? details,
        CancellationToken cancellationToken,
        string phaseStatus = "processing")
    {
        var createdAt = DateTime.UtcNow;

        try
        {
            await _publishEndpoint.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    request.UserId,
                    NotificationTypes.AiAccountAnalysisSuggestionProcessing,
                    title,
                    message,
                    new
                    {
                        correlationId = request.CorrelationId,
                        socialMediaId = request.SocialMediaId,
                        platform = "unknown",
                        status = "Processing",
                        taskStatus = "Processing",
                        phaseStatus,
                        action,
                        details,
                        isSuggested = false,
                        suggestion = (string?)null,
                        generatedAt = createdAt,
                        completedAt = (DateTime?)null,
                        errorCode = (string?)null,
                        errorMessage = (string?)null,
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
                "Failed to publish account analysis thinking notification. CorrelationId={CorrelationId} Action={Action}",
                request.CorrelationId,
                action);
        }
    }

    private async Task<RagQueryResponse?> SafeQueryAsync(
        Task<RagQueryResponse> task,
        string source,
        Guid socialMediaId)
    {
        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Optional RAG retrieval failed for socialMediaId={SocialMediaId} source={Source}",
                socialMediaId,
                source);
            return null;
        }
    }

    private static string BuildRagQuery(
        string platform,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? instruction)
    {
        var builder = new StringBuilder();
        builder.Append("Analyze this social account's recent posts, engagement metrics, grammar, caption quality, content angles, visual patterns, and audience response. ");
        builder.Append("Suggest what content to create next and what to fix for higher engagement. ");
        builder.Append("Focus on concrete problems in current posts and practical improvements. ");
        builder.Append("Target platform: ").Append(platform).Append(". ");

        if (from.HasValue || to.HasValue)
        {
            builder.Append("Requested analysis period: ");
            builder.Append(from?.ToString("u") ?? "beginning");
            builder.Append(" to ");
            builder.Append(to?.ToString("u") ?? "now");
            builder.Append(". ");
        }

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.Append("User instruction: ").Append(instruction.Trim());
        }

        return builder.ToString();
    }

    private static string BuildUserText(
        string platform,
        AnalysisSuggestionRequest request,
        SocialPlatformDashboardSummaryResponse summary,
        IReadOnlyList<SocialPlatformDashboardPostResponse> posts,
        SocialPlatformPostStatsResponse aggregatedStats,
        SocialPlatformPostAnalysisResponse aggregateAnalysis,
        RagMultimodalQueryResponse rag,
        IReadOnlyList<AnalysisSuggestionRagReference> references,
        RagQueryResponse? pageProfile,
        RagQueryResponse? formulaKnowledge,
        RagQueryResponse? engagementKnowledge,
        RagQueryResponse? algorithmKnowledge)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Target platform: {platform}");
        builder.AppendLine($"Social media id: {summary.SocialMediaId}");
        builder.AppendLine($"Analysis period: {FormatPeriod(request.From, request.To)}");
        builder.AppendLine($"Analyzed post count: {posts.Count}");
        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            builder.AppendLine($"User instruction: {request.Instruction!.Trim()}");
        }
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(pageProfile?.Answer))
        {
            builder.AppendLine("=== Account profile from RAG ===");
            builder.AppendLine(pageProfile!.Answer);
            builder.AppendLine();
        }

        if (summary.AccountInsights is not null)
        {
            builder.AppendLine("=== Account insights ===");
            builder.AppendLine(FormatAccountInsights(summary.AccountInsights));
            builder.AppendLine();
        }

        builder.AppendLine("=== Aggregated analytics for analyzed posts ===");
        builder.AppendLine(FormatStats(aggregatedStats));
        builder.AppendLine(FormatAnalysis(aggregateAnalysis));
        builder.AppendLine();

        if (posts.Count > 0)
        {
            builder.AppendLine("=== Recent posts and per-post analysis ===");
            for (var i = 0; i < posts.Count; i++)
            {
                var item = posts[i];
                builder.AppendLine($"[{i + 1}] postId={item.Post.PlatformPostId}");
                builder.AppendLine($"publishedAt={item.Post.PublishedAt?.ToString("u") ?? "unknown"} mediaType={item.Post.MediaType ?? "unknown"}");
                builder.AppendLine($"title={TrimForPrompt(item.Post.Title, 160)}");
                builder.AppendLine($"caption={TrimForPrompt(item.Post.Text ?? item.Post.Description, 500)}");
                builder.AppendLine($"stats={FormatStats(item.Post.Stats)}");
                builder.AppendLine($"analysis={FormatAnalysis(item.Analysis)}");
                if (!string.IsNullOrWhiteSpace(item.Post.Permalink ?? item.Post.ShareUrl))
                {
                    builder.AppendLine($"permalink={item.Post.Permalink ?? item.Post.ShareUrl}");
                }
                builder.AppendLine();
            }
        }
        else
        {
            builder.AppendLine("=== Recent posts and per-post analysis ===");
            builder.AppendLine("No posts matched the requested analysis period. Give a sparse-data recommendation and ask for a broader period if needed.");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(rag.Text?.Context))
        {
            builder.AppendLine("=== RAG context from account posts ===");
            builder.AppendLine(rag.Text!.Context);
            builder.AppendLine();
        }

        if (references.Count > 0)
        {
            builder.AppendLine("=== RAG references ===");
            for (var i = 0; i < references.Count; i++)
            {
                var reference = references[i];
                builder.AppendLine(
                    $"[{i + 1}] source={reference.Source} postId={reference.PostId ?? "n/a"} score={reference.Score?.ToString("F4") ?? "n/a"} caption={TrimForPrompt(reference.Caption, 220)}");
                if (!string.IsNullOrWhiteSpace(reference.VideoSegmentTime))
                {
                    builder.AppendLine($"videoTime={reference.VideoSegmentTime} transcript={TrimForPrompt(reference.VideoTranscript, 300)}");
                }
            }
            builder.AppendLine();
        }

        AppendKnowledge(builder, "Content formulas", formulaKnowledge);
        AppendKnowledge(builder, "Engagement triggers", engagementKnowledge);
        AppendKnowledge(builder, "Platform algorithm signals", algorithmKnowledge);

        builder.AppendLine("=== Output requirements ===");
        builder.AppendLine("Return strict JSON only. Do not output Markdown. Use this exact top-level shape:");
        builder.AppendLine("{\"summary\":\"...\",\"cards\":[{\"title\":\"Overall diagnosis\",\"tone\":\"diagnosis\",\"points\":[\"...\"]}],\"nextPostIdeas\":[{\"title\":\"...\",\"prompt\":\"...\",\"why\":\"...\"}],\"immediateAction\":\"...\"}");
        builder.AppendLine("Render every human-facing string in English only. Keep points concrete and short.");

        return builder.ToString();
    }

    private static void AppendKnowledge(StringBuilder builder, string title, RagQueryResponse? response)
    {
        if (string.IsNullOrWhiteSpace(response?.Answer))
        {
            return;
        }

        builder.AppendLine($"=== {title} ===");
        builder.AppendLine(response!.Answer);
        builder.AppendLine();
    }

    private static IReadOnlyList<SocialPlatformDashboardPostResponse> FilterPosts(
        IReadOnlyList<SocialPlatformDashboardPostResponse> posts,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var hasPeriodFilter = from.HasValue || to.HasValue;

        return posts
            .Where(item => !hasPeriodFilter || item.Post.PublishedAt.HasValue)
            .Where(item => !from.HasValue || item.Post.PublishedAt!.Value >= from.Value)
            .Where(item => !to.HasValue || item.Post.PublishedAt!.Value <= to.Value)
            .OrderByDescending(item => item.Post.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static SocialPlatformPostStatsResponse BuildAggregatedStats(
        IReadOnlyList<SocialPlatformDashboardPostResponse> posts)
    {
        var sourceStats = posts
            .Select(item => item.Post.Stats)
            .Where(item => item != null)
            .Select(item => item!)
            .ToList();

        if (sourceStats.Count == 0)
        {
            return new SocialPlatformPostStatsResponse(
                Views: 0,
                Reach: 0,
                Impressions: 0,
                Likes: 0,
                Comments: 0,
                Replies: 0,
                Shares: 0,
                Reposts: 0,
                Quotes: 0,
                TotalInteractions: 0,
                Saves: 0);
        }

        var hasSaves = sourceStats.Any(item => item.Saves.HasValue);

        return new SocialPlatformPostStatsResponse(
            Views: SumNullable(sourceStats, item => item.Views),
            Reach: SumNullable(sourceStats, item => item.Reach),
            Impressions: SumNullable(sourceStats, item => item.Impressions),
            Likes: sourceStats.Sum(item => item.Likes ?? 0),
            Comments: sourceStats.Sum(item => item.Comments ?? 0),
            Replies: sourceStats.Sum(item => item.Replies ?? 0),
            Shares: sourceStats.Sum(item => item.Shares ?? 0),
            Reposts: sourceStats.Sum(item => item.Reposts ?? 0),
            Quotes: sourceStats.Sum(item => item.Quotes ?? 0),
            TotalInteractions: sourceStats.Sum(item => item.TotalInteractions),
            Saves: hasSaves ? sourceStats.Sum(item => item.Saves ?? 0) : null);
    }

    private static long? SumNullable(
        IReadOnlyList<SocialPlatformPostStatsResponse> sourceStats,
        Func<SocialPlatformPostStatsResponse, long?> selector)
    {
        var values = sourceStats
            .Select(selector)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return values.Count == 0 ? null : values.Sum();
    }

    private static IReadOnlyList<AnalysisSuggestionRagReference> BuildRagReferences(
        RagMultimodalQueryResponse rag)
    {
        var references = new List<AnalysisSuggestionRagReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (rag.Text?.References is not null)
        {
            foreach (var reference in rag.Text.References)
            {
                var key = $"text:{reference.DocumentId}";
                if (!seen.Add(key))
                {
                    continue;
                }

                references.Add(new AnalysisSuggestionRagReference(
                    DocumentId: reference.DocumentId,
                    PostId: reference.PostId,
                    Source: "text",
                    Score: null,
                    Caption: FirstNonEmpty(reference.Caption, reference.Content),
                    ImageUrl: null));
            }
        }

        if (rag.Visual is not null)
        {
            foreach (var hit in rag.Visual)
            {
                var key = $"visual:{hit.DocumentId}:{hit.PostId}:{hit.ImageUrl}";
                if (!seen.Add(key))
                {
                    continue;
                }

                references.Add(new AnalysisSuggestionRagReference(
                    DocumentId: hit.DocumentId,
                    PostId: hit.PostId,
                    Source: "visual",
                    Score: hit.Score,
                    Caption: hit.Caption,
                    ImageUrl: hit.MirroredImageUrl ?? hit.ImageUrl));
            }
        }

        if (rag.Video is not null)
        {
            foreach (var hit in rag.Video)
            {
                var key = $"video:{hit.VideoName}:{hit.PostId}:{hit.Index}:{hit.Time}";
                if (!seen.Add(key))
                {
                    continue;
                }

                references.Add(new AnalysisSuggestionRagReference(
                    DocumentId: hit.VideoName,
                    PostId: hit.PostId,
                    Source: "video",
                    Score: hit.Score,
                    Caption: hit.Caption,
                    ImageUrl: hit.FrameUrl,
                    VideoSegmentTime: hit.Time,
                    VideoTranscript: hit.Transcript));
            }
        }

        return references;
    }

    private static IReadOnlyList<AnalysisSuggestionRetrievalError> BuildRetrievalErrors(
        RagMultimodalQueryResponse rag)
    {
        var errors = new List<AnalysisSuggestionRetrievalError>();
        if (!string.IsNullOrWhiteSpace(rag.VisualError))
        {
            errors.Add(new AnalysisSuggestionRetrievalError("visual", rag.VisualError!));
        }

        if (!string.IsNullOrWhiteSpace(rag.VideoError))
        {
            errors.Add(new AnalysisSuggestionRetrievalError("video", rag.VideoError!));
        }

        return errors;
    }

    private static string FormatAccountInsights(SocialPlatformAccountInsightsResponse insights)
    {
        var parts = new List<string>
        {
            $"accountId={insights.AccountId ?? "unknown"}",
            $"accountName={insights.AccountName ?? "unknown"}",
            $"username={insights.Username ?? "unknown"}",
            $"followers={FormatLong(insights.Followers)}",
            $"following={FormatLong(insights.Following)}",
            $"mediaCount={FormatLong(insights.MediaCount)}",
        };

        if (insights.Metadata is not null && insights.Metadata.Count > 0)
        {
            parts.Add("metadata=" + string.Join(", ", insights.Metadata.Select(item => $"{item.Key}:{item.Value}")));
        }

        return string.Join("; ", parts);
    }

    private static string FormatStats(SocialPlatformPostStatsResponse? stats)
    {
        if (stats is null)
        {
            return "no tracked stats";
        }

        var parts = new List<string>
        {
            $"views={FormatLong(stats.Views)}",
            $"reach={FormatLong(stats.Reach)}",
            $"impressions={FormatLong(stats.Impressions)}",
            $"likes={FormatLong(stats.Likes)}",
            $"comments={FormatLong(stats.Comments)}",
            $"replies={FormatLong(stats.Replies)}",
            $"shares={FormatLong(stats.Shares)}",
            $"reposts={FormatLong(stats.Reposts)}",
            $"quotes={FormatLong(stats.Quotes)}",
            $"saves={FormatLong(stats.Saves)}",
            $"totalInteractions={stats.TotalInteractions}",
        };

        if (stats.ReactionBreakdown is not null && stats.ReactionBreakdown.Count > 0)
        {
            parts.Add("reactions=" + string.Join(", ", stats.ReactionBreakdown.Select(item => $"{item.Key}:{item.Value}")));
        }

        if (stats.MetricBreakdown is not null && stats.MetricBreakdown.Count > 0)
        {
            parts.Add("metrics=" + string.Join(", ", stats.MetricBreakdown.Select(item => $"{item.Key}:{item.Value}")));
        }

        return string.Join("; ", parts);
    }

    private static string FormatAnalysis(SocialPlatformPostAnalysisResponse? analysis)
    {
        if (analysis is null)
        {
            return "no analysis available";
        }

        return
            $"performanceBand={analysis.PerformanceBand}; " +
            $"engagementRateByViews={FormatDecimal(analysis.EngagementRateByViews)}; " +
            $"conversationRateByViews={FormatDecimal(analysis.ConversationRateByViews)}; " +
            $"amplificationRateByViews={FormatDecimal(analysis.AmplificationRateByViews)}; " +
            $"approvalRateByViews={FormatDecimal(analysis.ApprovalRateByViews)}; " +
            $"highlights={string.Join(" | ", analysis.Highlights)}";
    }

    private static string FormatPeriod(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!from.HasValue && !to.HasValue)
        {
            return "latest available posts";
        }

        return $"{from?.ToString("u") ?? "beginning"} to {to?.ToString("u") ?? "now"}";
    }

    private static string NormalizePlatform(string? platform)
    {
        return string.IsNullOrWhiteSpace(platform)
            ? "unknown"
            : platform.Trim().ToLowerInvariant();
    }

    private static string FormatLong(long? value)
    {
        return value?.ToString() ?? "unknown";
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.##") ?? "unknown";
    }

    private static string TrimForPrompt(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength] + "...";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
