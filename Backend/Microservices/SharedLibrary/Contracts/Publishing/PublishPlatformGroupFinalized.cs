namespace SharedLibrary.Contracts.Publishing;

public class PublishPlatformGroupFinalized
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid PostId { get; set; }

    public string SocialMediaType { get; set; } = null!;

    public DateTime FinalizedAt { get; set; }
}
