namespace SharedLibrary.Contracts.Recommendations;

public sealed class GenerateDraftPostStarted
{
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public Guid SocialMediaId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string UserPrompt { get; set; } = null!;
    public int TopK { get; set; } = 6;
    public int MaxReferenceImages { get; set; } = 3;
    public int MaxRagPosts { get; set; } = 30;
    public DateTime StartedAt { get; set; }
}
