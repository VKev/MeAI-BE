using Application.Abstractions.Rag;
using Application.Posts.Models;

namespace Application.Recommendations.Models;

public sealed record AnalysisSuggestionRequest(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? PostLimit = null,
    int? TopK = null,
    int? MaxRagPosts = null,
    bool? RefreshIndex = null,
    string? Instruction = null);

public sealed record AnalysisSuggestionResponse(
    Guid SocialMediaId,
    string Platform,
    string Suggestion,
    string DocumentIdPrefix,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int AnalyzedPostCount,
    SocialPlatformPostStatsResponse AggregatedStats,
    SocialPlatformPostAnalysisResponse AggregateAnalysis,
    SocialPlatformAccountInsightsResponse? AccountInsights,
    IReadOnlyList<AnalysisSuggestionPostReference> Posts,
    IReadOnlyList<AnalysisSuggestionRagReference> References,
    IReadOnlyList<WebSource>? WebSources = null,
    IReadOnlyList<AnalysisSuggestionRetrievalError>? RetrievalErrors = null);

public sealed record AnalysisSuggestionPostReference(
    string PlatformPostId,
    string? Title,
    string? Text,
    string? MediaType,
    DateTimeOffset? PublishedAt,
    string? Permalink,
    SocialPlatformPostStatsResponse? Stats,
    SocialPlatformPostAnalysisResponse? Analysis);

public sealed record AnalysisSuggestionRagReference(
    string? DocumentId,
    string? PostId,
    string Source,
    double? Score,
    string? Caption,
    string? ImageUrl,
    string? VideoSegmentTime = null,
    string? VideoTranscript = null);

public sealed record AnalysisSuggestionRetrievalError(
    string Source,
    string Error);

public sealed record AnalysisSuggestionStatusResponse(
    Guid SocialMediaId,
    string Platform,
    string Status,
    bool IsSuggested,
    Guid? CorrelationId,
    string? Suggestion,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode,
    string? ErrorMessage);
