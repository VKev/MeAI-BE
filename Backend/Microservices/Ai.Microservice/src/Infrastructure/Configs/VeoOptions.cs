namespace Infrastructure.Configs;

public sealed class VeoOptions
{
    public const string SectionName = "Veo";

    public string ApiKey { get; set; } = string.Empty;

    public string CallbackUrl { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.kie.ai";
}
