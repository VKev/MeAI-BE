namespace SharedLibrary.Contracts.ImageGenerating;

public class ImageTaskCreated
{
    public Guid CorrelationId { get; set; }

    public string KieTaskId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
