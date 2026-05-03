namespace Infrastructure.Configs;

public sealed class GenerationStorageEstimates
{
    public const string SectionName = "GenerationStorageEstimates";

    public Dictionary<string, long> ImagesByResolutionMb { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1K"] = 5,
        ["2K"] = 12
    };

    public Dictionary<string, long> VideosByModelMb { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["veo3_fast"] = 150,
        ["veo3"] = 250,
        ["veo3_quality"] = 350
    };
}
