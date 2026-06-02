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
        ["gemini-omni-video"] = 150,
        ["grok-imagine-video-1-5-preview"] = 150,
        ["veo-3-1"] = 250,
        ["bytedance/seedance-2"] = 250
    };
}
