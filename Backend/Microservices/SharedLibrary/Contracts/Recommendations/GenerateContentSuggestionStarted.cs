namespace SharedLibrary.Contracts.Recommendations;

public sealed class GenerateContentSuggestionStarted
{
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public Guid SocialMediaId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string? Style { get; set; }
    public string? MediaType { get; set; }
    public string? Instruction { get; set; }
    public int? TopK { get; set; }
    public int? MaxRagPosts { get; set; }
    public bool? RefreshIndex { get; set; }
    public DateTime StartedAt { get; set; }
}
