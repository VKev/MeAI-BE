namespace SharedLibrary.Contracts.Recommendations;

public sealed class GenerateAnalysisSuggestionStarted
{
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public Guid SocialMediaId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int? PostLimit { get; set; }
    public int? TopK { get; set; }
    public int? MaxRagPosts { get; set; }
    public bool? RefreshIndex { get; set; }
    public string? Instruction { get; set; }
    public DateTime StartedAt { get; set; }
}
