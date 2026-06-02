namespace SharedLibrary.Contracts.VideoGenerating;

public class VideoGenerationStarted
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public string Prompt { get; set; } = null!;

    public List<string>? ImageUrls { get; set; }

    public string Model { get; set; } = "gemini-omni-video";

    public string? Variant { get; set; }

    public string? GenerationType { get; set; }

    public string AspectRatio { get; set; } = "16:9";

    public int? Seeds { get; set; }

    public bool EnableTranslation { get; set; } = true;

    public string? Watermark { get; set; }

    public string? Resolution { get; set; }

    public int? Duration { get; set; }

    public bool? GenerateAudio { get; set; }

    public bool? ReturnLastFrame { get; set; }

    public bool? WebSearch { get; set; }

    public DateTime CreatedAt { get; set; }
}
