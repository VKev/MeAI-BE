namespace SharedLibrary.Contracts.Recommendations;

/// <summary>
/// Published by the start command and consumed by the RecommendPostGenerationConsumer.
/// Sibling to <see cref="GenerateDraftPostStarted"/> but operates on an existing post.
/// </summary>
public sealed class GenerateRecommendPostStarted
{
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid OriginalPostId { get; set; }

    /// <summary>True if Step 3 (caption regen) should run.</summary>
    public bool ImproveCaption { get; set; }

    /// <summary>True if Step 4 (image-gen) should run.</summary>
    public bool ImproveImage { get; set; }

    /// <summary>"creative" | "branded" (default) | "marketing".</summary>
    public string Style { get; set; } = "branded";

    /// <summary>Optional free-form steering text from the user.</summary>
    public string? UserInstruction { get; set; }

    public DateTime StartedAt { get; set; }
}
