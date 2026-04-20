namespace Infrastructure.Configuration;

public sealed class FeedSeedOptions
{
    public const string SectionName = "FeedSeed";

    public bool Enabled { get; set; }

    public string DataRoot { get; set; } = "/seed-data/feed";

    public string PublicBaseUrl { get; set; } = "http://localhost:2406";
}
