namespace SharedLibrary.Contracts.Publishing;

public class UpdatePublishedTargetRequested
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid PostId { get; set; }

    public Guid PublicationId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string SocialMediaType { get; set; } = null!;

    public string ExternalContentId { get; set; } = null!;

    public string? DestinationOwnerId { get; set; }

    public string NewCaption { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
