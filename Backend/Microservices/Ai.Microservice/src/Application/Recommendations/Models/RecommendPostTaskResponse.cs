namespace Application.Recommendations.Models;

public sealed record RecommendPostTaskResponse(
    Guid RecommendId,
    Guid CorrelationId,
    string Status,
    Guid OriginalPostId,
    Guid UserId,
    Guid? WorkspaceId,
    bool ImproveCaption,
    bool ImproveImage,
    string Style,
    string? UserInstruction,
    string? ResultCaption,
    Guid? ResultResourceId,
    string? ResultPresignedUrl,
    IReadOnlyList<Guid> ResultResourceIds,
    IReadOnlyList<string> ResultPresignedUrls,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record StartImprovePostRequest(
    /// <summary>True if the caption should be regenerated. At least one of
    /// <see cref="ImproveCaption"/> / <see cref="ImproveImage"/> must be true; the
    /// command rejects requests where both are false.</summary>
    bool ImproveCaption = false,
    /// <summary>True if the image should be regenerated.</summary>
    bool ImproveImage = false,
    /// <summary>"creative" | "branded" | "marketing". Optional. When omitted, the
    /// improve flow inherits the original post's stored style (falling back to
    /// "branded").</summary>
    string? Style = null,
    /// <summary>Optional platform hint ("facebook" | "instagram" | "tiktok" |
    /// "threads"). Used when no explicit or original SocialMediaId can provide the
    /// platform context.</summary>
    string? Platform = null,
    /// <summary>Optional connected account id to use as the RAG/profile context for
    /// the improvement. When provided, this account is validated against the user and
    /// takes precedence over the original post's stored SocialMediaId.</summary>
    Guid? SocialMediaId = null,
    /// <summary>Optional free-form steering text from the user (e.g.
    /// "make the caption more playful", "use a cooler color palette in the image").
    /// Forwarded into both the caption and image-brief prompts when present.</summary>
    string? UserInstruction = null);
