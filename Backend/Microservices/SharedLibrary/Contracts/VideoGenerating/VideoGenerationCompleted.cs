namespace SharedLibrary.Contracts.VideoGenerating;

public class VideoGenerationCompleted
{
    public Guid CorrelationId { get; set; }

    public string VeoTaskId { get; set; } = null!;

    public string ResultUrls { get; set; } = null!;

    public string? OriginUrls { get; set; }

    public string? Resolution { get; set; }

    public DateTime CompletedAt { get; set; }
}
