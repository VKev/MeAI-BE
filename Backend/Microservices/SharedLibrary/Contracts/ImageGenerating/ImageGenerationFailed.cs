namespace SharedLibrary.Contracts.ImageGenerating;

public class ImageGenerationFailed
{
    public Guid CorrelationId { get; set; }

    public string? KieTaskId { get; set; }

    public int ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime FailedAt { get; set; }
}
