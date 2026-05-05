namespace Application.Recommendations.Models;

public sealed record RecommendPostTaskResponse(
    Guid Id,
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
    /// <summary>Optional free-form steering text from the user (e.g.
    /// "make the caption more playful", "use a cooler color palette in the image").
    /// Forwarded into both the caption and image-brief prompts when present.</summary>
    string? UserInstruction = null);
