namespace Application.Recommendations.Models;

public sealed record AccountRecommendationsQueryRequest(
    string Query,
    int? TopK = null,
    string? Mode = null);
