namespace Infrastructure.Logic.ApiCredentials;

public static class ApiCredentialCatalog
{
    public const string ServiceName = "Ai";

    public static readonly IReadOnlyList<ApiCredentialDefinition> Definitions =
    [
        new("Gemini", "ApiKey", "Gemini API key", "Gemini:ApiKey", "Gemini__ApiKey"),
        new("Kie", "ApiKey", "Kie API key", "Kie:ApiKey", "Kie__ApiKey"),
        new("N8n", "InternalCallbackToken", "n8n internal callback token", "N8n:InternalCallbackToken", "N8n__InternalCallbackToken")
    ];

    public static ApiCredentialDefinition? Find(string provider, string keyName)
    {
        return Definitions.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.KeyName, keyName, StringComparison.OrdinalIgnoreCase));
    }
}
