namespace Infrastructure.Logic.ApiCredentials;

public static class ApiCredentialCatalog
{
    public const string ServiceName = "Ai";

    public static readonly IReadOnlyList<ApiCredentialDefinition> Definitions =
    [
        new("Gemini", "ApiKey", "Gemini API key", "Gemini:ApiKey", "Gemini__ApiKey"),
        new("Kie", "ApiKey", "Kie API key", "Kie:ApiKey", "Kie__ApiKey"),
        new("OpenRouter", "ApiKey", "OpenRouter API key", "Rag:MultimodalLlmApiKey", "RAG_MULTIMODAL_LLM_API_KEY")
    ];

    public static ApiCredentialDefinition? Find(string provider, string keyName)
    {
        return Definitions.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.KeyName, keyName, StringComparison.OrdinalIgnoreCase));
    }
}
