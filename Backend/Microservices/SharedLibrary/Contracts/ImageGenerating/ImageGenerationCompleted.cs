namespace SharedLibrary.Contracts.ImageGenerating;

public class ImageGenerationCompleted
{
    public Guid CorrelationId { get; set; }

    public string? KieTaskId { get; set; }

    public List<string>? ResultUrls { get; set; }

    public DateTime CompletedAt { get; set; }
}
