namespace SharedLibrary.Contracts.SocialMedia;

public sealed class SyncSocialMediaPostsRequested
{
    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid SocialMediaId { get; set; }

    public string Platform { get; set; } = null!;

    public string Trigger { get; set; } = "oauth_callback";

    public int PageLimit { get; set; } = 50;

    public int MaxPages { get; set; } = 100;

    public DateTime RequestedAt { get; set; }
}
