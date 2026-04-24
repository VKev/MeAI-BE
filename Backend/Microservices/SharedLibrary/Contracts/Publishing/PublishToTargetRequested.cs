namespace SharedLibrary.Contracts.Publishing;

public class PublishToTargetRequested
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid PostId { get; set; }

    public Guid SocialMediaId { get; set; }

    public Guid PublicationId { get; set; }

    public Guid? PublishingScheduleId { get; set; }

    public string SocialMediaType { get; set; } = null!;

    public bool? IsPrivate { get; set; }

    public int AttemptNumber { get; set; }

    public DateTime CreatedAt { get; set; }
}
