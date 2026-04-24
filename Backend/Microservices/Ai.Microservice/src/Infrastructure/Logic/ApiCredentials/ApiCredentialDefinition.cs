namespace Infrastructure.Logic.ApiCredentials;

public sealed record ApiCredentialDefinition(
    string Provider,
    string KeyName,
    string DisplayName,
    params string[] ConfigurationKeys);
