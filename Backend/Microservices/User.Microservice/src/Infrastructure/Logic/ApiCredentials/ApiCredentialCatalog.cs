namespace Infrastructure.Logic.ApiCredentials;

public static class ApiCredentialCatalog
{
    public const string ServiceName = "User";

    public static readonly IReadOnlyList<ApiCredentialDefinition> Definitions =
    [
        new("Stripe", "PublishableKey", "Stripe publishable key", "Stripe:PublishableKey", "Stripe__PublishableKey"),
        new("Stripe", "SecretKey", "Stripe secret key", "Stripe:SecretKey", "Stripe__SecretKey"),
        new("Stripe", "WebhookSecret", "Stripe webhook secret", "Stripe:WebhookSecret", "Stripe__WebhookSecret"),
        new("Facebook", "AppId", "Facebook app id", "Facebook:AppId", "Facebook__AppId"),
        new("Facebook", "AppSecret", "Facebook app secret", "Facebook:AppSecret", "Facebook__AppSecret"),
        new("Instagram", "AppId", "Instagram app id", "Instagram:AppId", "Instagram__AppId"),
        new("Instagram", "AppSecret", "Instagram app secret", "Instagram:AppSecret", "Instagram__AppSecret"),
        new("TikTok", "ClientKey", "TikTok client key", "TikTok:ClientKey", "TikTok__ClientKey"),
        new("TikTok", "ClientSecret", "TikTok client secret", "TikTok:ClientSecret", "TikTok__ClientSecret"),
        new("Threads", "AppId", "Threads app id", "Threads:AppId", "Threads__AppId"),
        new("Threads", "AppSecret", "Threads app secret", "Threads:AppSecret", "Threads__AppSecret")
    ];

    public static ApiCredentialDefinition? Find(string provider, string keyName)
    {
        return Definitions.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.KeyName, keyName, StringComparison.OrdinalIgnoreCase));
    }
}
