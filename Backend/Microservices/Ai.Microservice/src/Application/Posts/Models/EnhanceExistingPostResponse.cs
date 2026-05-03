namespace Application.Posts.Models;

public sealed record EnhanceExistingPostResponse(
    Guid PostId,
    string Platform,
    IReadOnlyList<Guid> ResourceIds,
    EnhancedPostSuggestionResponse BestSuggestion,
    IReadOnlyList<EnhancedPostSuggestionResponse> Alternatives);

public sealed record EnhancedPostSuggestionResponse(
    string Caption,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> TrendingHashtags,
    string? CallToAction);
