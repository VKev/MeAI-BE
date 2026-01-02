namespace Infrastructure.Configs;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string PublishableKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string Currency { get; set; } = "vnd";

    public int CurrencyDecimals { get; set; } = 0;
}
