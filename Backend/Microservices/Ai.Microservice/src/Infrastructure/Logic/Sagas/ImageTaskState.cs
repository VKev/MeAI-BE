using MassTransit;

namespace Infrastructure.Logic.Sagas;

public class ImageTaskState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = null!;

    public int Version { get; set; }

    public Guid? TimeoutTokenId { get; set; }

    public Guid ImageTaskId { get; set; }

    public string? KieTaskId { get; set; }

    public string Prompt { get; set; } = null!;

    public string AspectRatio { get; set; } = "1:1";

    public string Resolution { get; set; } = "1K";

    public string OutputFormat { get; set; } = "png";

    public List<string>? ImageUrls { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
