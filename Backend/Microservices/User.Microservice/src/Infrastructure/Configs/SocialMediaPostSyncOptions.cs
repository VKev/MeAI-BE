namespace Infrastructure.Configs;

public sealed class SocialMediaPostSyncOptions
{
    public const string SectionName = "SocialMediaPostSync";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;

    public int InitialDelaySeconds { get; set; } = 30;

    public int PageLimit { get; set; } = 50;

    public int MaxPages { get; set; } = 2;

    public int MaxTargetsPerRun { get; set; } = 500;

    public bool SuppressSuccessNotifications { get; set; } = true;
}
