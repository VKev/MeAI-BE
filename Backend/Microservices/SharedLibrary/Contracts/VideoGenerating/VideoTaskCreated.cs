namespace SharedLibrary.Contracts.VideoGenerating;

public class VideoTaskCreated
{
    public Guid CorrelationId { get; set; }

    public string VeoTaskId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
