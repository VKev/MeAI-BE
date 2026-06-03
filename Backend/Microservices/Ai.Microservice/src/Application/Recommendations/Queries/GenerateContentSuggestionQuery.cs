using System.Text;
using Application.Abstractions.Rag;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Queries;

public sealed record GenerateContentSuggestionQuery(
    Guid CorrelationId,
    Guid UserId,
    Guid SocialMediaId,
    ContentSuggestionRequest Request) : IRequest<Result<ContentSuggestionResponse>>;

public sealed class GenerateContentSuggestionQueryHandler
    : IRequestHandler<GenerateContentSuggestionQuery, Result<ContentSuggestionResponse>>
{
#pragma warning disable CS0169
    private readonly RecommendPost? _domainDependency;
#pragma warning restore CS0169

    private const int DefaultTopK = 6;
    private const int MaxTopK = 20;
    private const int DefaultMaxRagPosts = 30;
    private const int MaxRagPosts = 200;

    private const string PromptWriterSystemPrompt =
        "You write the exact user prompt that will be sent into an AI social-post draft generator. " +
        "Use the supplied recommendation, RAG account context, past-post references, and web sources. " +
        "Return ONLY the prompt text, not JSON, not markdown headings. " +
        "The prompt must ask for one specific next post idea that is fresh for the account and not a duplicate of recent posts. " +
        "Include the concrete topic, why it is timely/current when web sources exist, the non-duplicate angle, the desired caption direction, " +
        "visual direction for the requested media type, and one bold or crazy creative twist that still fits the account. " +
        "Write in the account's primary language. Keep it practical enough that another AI can generate the final draft from it.";

    private readonly IMediator _mediator;
    private readonly IRagClient _ragClient;
    private readonly IMultimodalLlmClient _multimodalLlm;
    private readonly ILogger<GenerateContentSuggestionQueryHandler> _logger;

    public GenerateContentSuggestionQueryHandler(
        IMediator mediator,
        IRagClient ragClient,
        IMultimodalLlmClient multimodalLlm,
        ILogger<GenerateContentSuggestionQueryHandler> logger)
    {
        _mediator = mediator;
        _ragClient = ragClient;
        _multimodalLlm = multimodalLlm;
        _logger = logger;
    }

    public async Task<Result<ContentSuggestionResponse>> Handle(
        GenerateContentSuggestionQuery request,
        CancellationToken cancellationToken)
    {
        if (!DraftPostStyles.TryValidate(request.Request.Style, out var style))
        {
            return Result.Failure<ContentSuggestionResponse>(
                new Error("ContentSuggestion.InvalidStyle", "Unsupported content style."));
        }

        if (!DraftPostMediaTypes.TryValidate(request.Request.MediaType, out var mediaType))
        {
            return Result.Failure<ContentSuggestionResponse>(
                new Error("ContentSuggestion.InvalidMediaType", "Unsupported media type."));
        }

        var topK = Math.Clamp(request.Request.TopK ?? DefaultTopK, 1, MaxTopK);
        var maxRagPosts = Math.Clamp(request.Request.MaxRagPosts ?? DefaultMaxRagPosts, 1, MaxRagPosts);

        try
        {
            await _ragClient.WaitForRagReadyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG readiness check failed for content suggestion socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<ContentSuggestionResponse>(
                new Error("ContentSuggestion.RagUnavailable", $"RAG service is not ready: {ex.Message}"));
        }

        IndexSocialAccountPostsResponse? indexResponse = null;
        if (request.Request.RefreshIndex != false)
        {
            var indexResult = await _mediator.Send(
                new IndexSocialAccountPostsCommand(
                    request.UserId,
                    request.SocialMediaId,
                    maxRagPosts),
                cancellationToken);

            if (indexResult.IsFailure)
            {
                return Result.Failure<ContentSuggestionResponse>(indexResult.Error);
            }

            indexResponse = indexResult.Value;
        }

        var recommendationQuery = BuildRecommendationQuery(mediaType, style, request.Request.Instruction);
        var recommendationResult = await _mediator.Send(
            new QueryAccountRecommendationsQuery(
                request.UserId,
                request.SocialMediaId,
                recommendationQuery,
                topK),
            cancellationToken);

        if (recommendationResult.IsFailure)
        {
            return Result.Failure<ContentSuggestionResponse>(recommendationResult.Error);
        }

        var recommendation = recommendationResult.Value;
        var platform = indexResponse is null
            ? ExtractPlatform(recommendation.DocumentIdPrefix)
            : NormalizePlatform(indexResponse.Platform);
        var userText = BuildPromptWriterUserText(
            platform,
            mediaType,
            style,
            request.Request.Instruction,
            recommendation,
            indexResponse);

        MultimodalAnswerResult promptResult;
        try
        {
            promptResult = await _multimodalLlm.GenerateAnswerAsync(
                new MultimodalAnswerRequest(
                    SystemPrompt: PromptWriterSystemPrompt,
                    UserText: userText,
                    ReferenceImageUrls: null,
                    MaxOutputTokens: 700,
                    WebSearchEnabled: true),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content suggestion prompt LLM failed for socialMediaId={SocialMediaId}", request.SocialMediaId);
            return Result.Failure<ContentSuggestionResponse>(
                new Error("ContentSuggestion.LlmFailed", $"Content suggestion failed: {ex.Message}"));
        }

        var userPrompt = CleanPrompt(promptResult.Answer);
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return Result.Failure<ContentSuggestionResponse>(
                new Error("ContentSuggestion.EmptyPrompt", "AI returned an empty content suggestion."));
        }

        return Result.Success(new ContentSuggestionResponse(
            CorrelationId: request.CorrelationId,
            SocialMediaId: request.SocialMediaId,
            UserId: request.UserId,
            WorkspaceId: request.Request.WorkspaceId,
            Platform: platform,
            Style: style,
            MediaType: mediaType,
            UserPrompt: userPrompt,
            RecommendationSummary: recommendation.Answer,
            DocumentIdPrefix: recommendation.DocumentIdPrefix,
            GeneratedAt: DateTimeOffset.UtcNow,
            WebSources: MergeWebSources(recommendation.WebSources, promptResult.Sources),
            References: recommendation.References
                .Take(8)
                .Select(reference => new ContentSuggestionReference(
                    reference.DocumentId,
                    reference.PostId,
                    reference.Source,
                    reference.Caption,
                    reference.Score))
                .ToList(),
            RetrievalErrors: recommendation.RetrievalErrors?
                .Select(error => new ContentSuggestionRetrievalError(error.Source, error.Error))
                .ToList()));
    }

    private static string BuildRecommendationQuery(string mediaType, string style, string? instruction)
    {
        var builder = new StringBuilder();
        builder.Append("Suggest the next content idea for this account. ");
        builder.Append("Use RAG from the account profile and recent posts to understand the niche, voice, audience, recurring themes, and visual style. ");
        builder.Append("Use fresh web search to find current news, product launches, seasonal hooks, or trending conversations inside that niche. ");
        builder.Append("The selected idea must not duplicate the same subject, model, event, offer, audience problem, hook, or angle from recent posts. ");
        builder.Append("Return one concrete, on-brand idea with a clear non-duplicate angle and include one bold creative twist. ");
        builder.Append("Target media type: ").Append(mediaType).Append(". Style: ").Append(style).Append(". ");
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.Append("User instruction: ").Append(instruction.Trim()).Append(". ");
        }
        return builder.ToString();
    }

    private static string BuildPromptWriterUserText(
        string platform,
        string mediaType,
        string style,
        string? instruction,
        AccountRecommendationsAnswer recommendation,
        IndexSocialAccountPostsResponse? indexResponse)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Platform: {platform}");
        builder.AppendLine($"Requested media type: {mediaType}");
        builder.AppendLine($"Requested style: {style}");
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            builder.AppendLine($"User instruction: {instruction.Trim()}");
        }
        builder.AppendLine();

        if (indexResponse is not null)
        {
            builder.AppendLine("=== Account indexing summary ===");
            builder.AppendLine($"Posts scanned: {indexResponse.TotalPostsScanned}; new: {indexResponse.NewPosts}; updated: {indexResponse.UpdatedPosts}; unchanged: {indexResponse.UnchangedPosts}");
            if (indexResponse.IndexedKnowledgeItems is { Count: > 0 })
            {
                builder.AppendLine("Recent indexed posts to avoid duplicating:");
                foreach (var item in indexResponse.IndexedKnowledgeItems.Take(12))
                {
                    builder.AppendLine($"- {TrimForPrompt(item.Title, 100)} | {TrimForPrompt(item.TextPreview, 180)} | media={item.MediaType ?? "unknown"} | published={item.PublishedAt?.ToString("u") ?? "unknown"}");
                }
            }
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(recommendation.PageProfileText))
        {
            builder.AppendLine("=== Page profile ===");
            builder.AppendLine(recommendation.PageProfileText);
            builder.AppendLine();
        }

        builder.AppendLine("=== Strategy recommendation ===");
        builder.AppendLine(recommendation.Answer);
        builder.AppendLine();

        if (recommendation.References.Count > 0)
        {
            builder.AppendLine("=== Recent post/RAG references to avoid copying too closely ===");
            foreach (var reference in recommendation.References.Take(10))
            {
                builder.AppendLine($"- source={reference.Source} postId={reference.PostId ?? "n/a"} score={reference.Score:F4} caption={TrimForPrompt(reference.Caption, 240)}");
                if (!string.IsNullOrWhiteSpace(reference.VideoTranscript))
                {
                    builder.AppendLine($"  video={reference.VideoSegmentTime ?? "unknown"} transcript={TrimForPrompt(reference.VideoTranscript, 240)}");
                }
            }
            builder.AppendLine();
        }

        if (recommendation.WebSources is { Count: > 0 })
        {
            builder.AppendLine("=== Web sources used for freshness ===");
            foreach (var source in recommendation.WebSources.Take(8))
            {
                builder.AppendLine($"- {source.Title ?? source.Url}: {source.Url} {TrimForPrompt(source.Snippet, 180)}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("=== Output ===");
        builder.AppendLine("Write the final prompt that should be placed into the AI Recommendation prompt box. It must be specific enough to generate the next post now.");
        return builder.ToString();
    }

    private static string CleanPrompt(string value)
    {
        var cleaned = value.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Trim('`').Trim();
        }
        return cleaned.Trim('"').Trim();
    }

    private static IReadOnlyList<WebSource>? MergeWebSources(
        IReadOnlyList<WebSource>? first,
        IReadOnlyList<WebSource>? second)
    {
        var merged = new List<WebSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in (first ?? Array.Empty<WebSource>()).Concat(second ?? Array.Empty<WebSource>()))
        {
            if (string.IsNullOrWhiteSpace(source.Url) || !seen.Add(source.Url))
            {
                continue;
            }
            merged.Add(source);
        }
        return merged.Count > 0 ? merged : null;
    }

    private static string ExtractPlatform(string documentIdPrefix)
    {
        var index = documentIdPrefix.IndexOf(':', StringComparison.Ordinal);
        return index > 0 ? documentIdPrefix[..index] : "unknown";
    }

    private static string NormalizePlatform(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }

    private static string TrimForPrompt(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }
}
