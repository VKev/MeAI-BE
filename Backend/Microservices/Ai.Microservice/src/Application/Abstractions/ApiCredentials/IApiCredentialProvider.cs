namespace Application.Abstractions.ApiCredentials;

public interface IApiCredentialProvider
{
    string GetRequiredValue(string provider, string keyName);

    string? GetOptionalValue(string provider, string keyName);

    void StoreValue(string provider, string keyName, string? value);

    void Invalidate(string provider, string keyName);
}
