using MassTransit;

namespace Infrastructure.Sagas;

public class VideoTaskState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = null!;

    public int Version { get; set; }

    public Guid? TimeoutTokenId { get; set; }

    public Guid VideoTaskId { get; set; }

    public string? VeoTaskId { get; set; }

    public string Prompt { get; set; } = null!;

    public string Model { get; set; } = "veo3_fast";

    public string AspectRatio { get; set; } = "16:9";

    public List<string>? ImageUrls { get; set; }

    public string? GenerationType { get; set; }

    public int? Seeds { get; set; }

    public bool EnableTranslation { get; set; } = true;

    public string? Watermark { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
