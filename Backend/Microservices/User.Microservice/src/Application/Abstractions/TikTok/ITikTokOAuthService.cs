using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.TikTok;

public interface ITikTokOAuthService
{
    (string AuthorizationUrl, string State, string CodeVerifier) GenerateAuthorizationUrl(Guid userId, string scopes);

    Task<Result<TikTokTokenResponse>> ExchangeCodeForTokenAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    Task<Result<TikTokTokenResponse>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    bool TryValidateState(string state, out Guid userId);

    Task<Result<TikTokUserProfile>> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed class TikTokUserProfile
{
    public string? OpenId { get; set; }
    public string? UnionId { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? BioDescription { get; set; }
    public int? FollowerCount { get; set; }
    public int? FollowingCount { get; set; }
}

public sealed class TikTokTokenResponse
{
    public string OpenId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public int RefreshExpiresIn { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
}

public sealed class TikTokErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string ErrorDescription { get; set; } = string.Empty;
    public string LogId { get; set; } = string.Empty;
}
