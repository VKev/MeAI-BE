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
        CancellationToken cancellationToken,
        string? preferredPageId = null);

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

    [JsonPropertyName("picture")]
    public FacebookProfilePictureResponse? Picture { get; set; }

    [JsonIgnore]
    public string? ProfilePictureUrl => Picture?.Data?.Url;

    [JsonIgnore]
    public string? PageId { get; set; }

    [JsonIgnore]
    public string? PageName { get; set; }

    [JsonIgnore]
    public string? PageAccessToken { get; set; }

    [JsonIgnore]
    public int? PageLikeCount { get; set; }

    [JsonIgnore]
    public int? PageFollowerCount { get; set; }

    [JsonIgnore]
    public int? PagePostCount { get; set; }

    [JsonIgnore]
    public IReadOnlyList<FacebookPageProfile> Pages { get; set; } = [];
}

public sealed record FacebookPageProfile(
    string Id,
    string? Name,
    string? AccessToken,
    int? FanCount,
    int? FollowersCount,
    int? PostCount);

public sealed class FacebookProfilePictureResponse
{
    [JsonPropertyName("data")]
    public FacebookProfilePictureDataResponse? Data { get; set; }
}

public sealed class FacebookProfilePictureDataResponse
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("is_silhouette")]
    public bool? IsSilhouette { get; set; }
}
