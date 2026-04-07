namespace Infrastructure.Configuration;

public sealed class ConfigSeedOptions
{
    public const string SectionName = "ConfigSeed";

    public string? ChatModel { get; set; }

    public string? MediaAspectRatio { get; set; } = "1:1";

    public int? NumberOfVariances { get; set; } = 1;
}
