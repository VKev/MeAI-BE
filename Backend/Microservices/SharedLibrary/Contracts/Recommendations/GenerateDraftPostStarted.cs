namespace SharedLibrary.Contracts.Recommendations;

public sealed class GenerateDraftPostStarted
{
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public Guid SocialMediaId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string UserPrompt { get; set; } = null!;
    /// <summary>True when the user did not provide a topic and the consumer should
    /// auto-discover one via page-content RAG + web search. <see cref="UserPrompt"/>
    /// will hold a placeholder marker like "[auto-discovered topic]" in this case.</summary>
    public bool IsAutoTopic { get; set; }
    /// <summary>"creative" | "branded" (default) | "marketing". Controls which
    /// image-design knowledge namespace the consumer RAGs for the brief, and how
    /// aggressively the caption pushes brand contact info.</summary>
    public string Style { get; set; } = "branded";
    public int TopK { get; set; } = 6;
    public int MaxReferenceImages { get; set; } = 3;
    public int MaxRagPosts { get; set; } = 30;
    public DateTime StartedAt { get; set; }
}
