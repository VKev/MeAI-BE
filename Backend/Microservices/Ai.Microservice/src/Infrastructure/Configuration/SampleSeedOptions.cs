namespace Infrastructure.Configuration;

public sealed class SampleSeedOptions
{
    public const string SectionName = "SampleSeed";

    public bool Enabled { get; set; }

    public string DataRoot { get; set; } = "/seed-data";
}
