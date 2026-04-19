namespace SharedLibrary.Contracts.ImageGenerating;

public class ImageGenerationStarted
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public string Prompt { get; set; } = null!;

    public List<string>? ImageUrls { get; set; }

    public string? Model { get; set; }

    public string AspectRatio { get; set; } = "1:1";

    public string Resolution { get; set; } = "1K";

    public string OutputFormat { get; set; } = "png";

    public int NumberOfVariances { get; set; } = 1;

    public DateTime CreatedAt { get; set; }
}
