namespace Application.Recommendations.Models;

public sealed record DraftPostTaskResponse(
    Guid CorrelationId,
    string Status,
    Guid SocialMediaId,
    Guid UserId,
    Guid? WorkspaceId,
    string UserPrompt,
    bool IsAutoTopic,
    string Style,
    Guid? ResultPostBuilderId,
    Guid? ResultPostId,
    Guid? ResultResourceId,
    string? ResultPresignedUrl,
    string? ResultCaption,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record StartDraftPostGenerationRequest(
    /// <summary>Optional. Specific topic for the next post. If omitted / null / empty,
    /// the AI auto-discovers a topic by analyzing the page's content pillars (RAG'd
    /// from the page profile + past posts) and web-searching for what's currently
    /// trending in those pillars. The chosen topic must be on-brand AND timely.</summary>
    string? UserPrompt = null,
    /// <summary>"creative" | "branded" | "marketing". Optional. Defaults to "branded"
    /// when omitted — the safe all-purpose middle ground (subtle brand + optional
    /// short headline). Use "creative" for pure mood/lifestyle posts (no on-image
    /// text), "marketing" for full promo (logo + headline + CTA + contact rendered).</summary>
    string? Style = null,
    Guid? WorkspaceId = null,
    int? TopK = null,
    int? MaxReferenceImages = null,
    int? MaxRagPosts = null);
