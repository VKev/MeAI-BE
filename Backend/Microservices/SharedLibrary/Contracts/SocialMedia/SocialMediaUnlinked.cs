namespace SharedLibrary.Contracts.SocialMedia;

public sealed class SocialMediaUnlinked
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string Platform { get; set; } = null!;

    public DateTime RequestedAt { get; set; }
}
