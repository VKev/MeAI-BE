namespace SharedLibrary.Contracts.VideoGenerating;

public class VideoGenerationFailed
{
    public Guid CorrelationId { get; set; }

    public string? VeoTaskId { get; set; }

    public int ErrorCode { get; set; }

    public string ErrorMessage { get; set; } = null!;

    public DateTime FailedAt { get; set; }
}
