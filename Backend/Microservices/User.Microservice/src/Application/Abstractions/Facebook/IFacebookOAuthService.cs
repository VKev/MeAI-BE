using System.Text.Json.Serialization;
using Application.Abstractions.Meta;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Facebook;

public interface IFacebookOAuthService
{
    (string AuthorizationUrl, string State) GenerateAuthorizationUrl(Guid userId, string? scopes);

    Task<Result<FacebookAccessTokenResponse>> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken);

    Task<Result<MetaDebugToken>> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<Result<FacebookProfileResponse>> FetchProfileAsync(
        string accessToken,
        CancellationToken cancellationToken);

    bool TryValidateState(string state, out Guid userId);
}

public sealed class FacebookAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class FacebookProfileResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
