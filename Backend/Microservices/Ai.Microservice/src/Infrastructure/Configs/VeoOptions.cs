namespace Infrastructure.Configs;

public sealed class VeoOptions
{
    public const string SectionName = "Kie";

    public string ApiKey { get; set; } = string.Empty;

    public string CallbackUrl { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.kie.ai";

    public int CreditLookupTimeoutSeconds { get; set; } = 5;
}
