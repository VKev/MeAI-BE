using System.Text.Json.Serialization;
using Application.Abstractions.Meta;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Instagram;

public interface IInstagramOAuthService
{
    (string AuthorizationUrl, string State) GenerateAuthorizationUrl(Guid userId, string? scopes);

    Task<Result<InstagramAccessTokenResponse>> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken);

    Task<Result<MetaDebugToken>> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<Result<InstagramGraphProfileResult>> FetchBusinessProfileAsync(
        string userAccessToken,
        MetaDebugToken? debugToken,
        CancellationToken cancellationToken);

    bool TryValidateState(string state, out Guid userId);
}

public sealed class InstagramAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

public sealed class InstagramProfile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

public sealed class InstagramGraphProfileResult
{
    public InstagramProfile Profile { get; set; } = new();
    public string PageId { get; set; } = string.Empty;
    public string? PageName { get; set; }
    public string PageAccessToken { get; set; } = string.Empty;
    public string InstagramAccountId { get; set; } = string.Empty;
    public string? InstagramAccountType { get; set; }
}
