namespace SharedLibrary.Contracts.VideoGenerating;

public class VideoExtensionStarted
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public string OriginalVeoTaskId { get; set; } = null!;

    public string Prompt { get; set; } = null!;

    public int? Seeds { get; set; }

    public string? Watermark { get; set; }

    public DateTime CreatedAt { get; set; }
}
