namespace SharedLibrary.Configs;

public sealed class BillingCurrencyOptions
{
    public const string SectionName = "Stripe";

    public string Currency { get; set; } = "vnd";

    public int CurrencyDecimals { get; set; } = 0;
}
