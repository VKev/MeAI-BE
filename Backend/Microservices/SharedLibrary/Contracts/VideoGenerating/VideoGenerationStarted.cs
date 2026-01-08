namespace SharedLibrary.Contracts.VideoGenerating;

public class VideoGenerationStarted
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public string Prompt { get; set; } = null!;

    public List<string>? ImageUrls { get; set; }

    public string Model { get; set; } = "veo3_fast";

    public string? GenerationType { get; set; }

    public string AspectRatio { get; set; } = "16:9";

    public int? Seeds { get; set; }

    public bool EnableTranslation { get; set; } = true;

    public string? Watermark { get; set; }

    public DateTime CreatedAt { get; set; }
}
