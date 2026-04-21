namespace SharedLibrary.Contracts.ImageGenerating;

public class ImageReframeRequested
{
    public Guid CorrelationId { get; set; }

    public Guid ParentCorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public string SourceImageUrl { get; set; } = null!;

    public string TargetRatio { get; set; } = "1:1";

    public SocialTargetDto? SocialTarget { get; set; }

    public DateTime CreatedAt { get; set; }
}
