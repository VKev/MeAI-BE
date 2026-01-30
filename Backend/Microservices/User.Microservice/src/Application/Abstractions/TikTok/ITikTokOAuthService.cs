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

    // Content Posting API
    Task<Result<TikTokVideoInitResponse>> InitiateVideoPublishAsync(
        string accessToken,
        TikTokPostInfo postInfo,
        TikTokVideoSourceInfo sourceInfo,
        CancellationToken cancellationToken);

    Task<Result<bool>> UploadVideoFileAsync(
        string uploadUrl,
        Stream videoStream,
        long videoSize,
        string contentType,
        CancellationToken cancellationToken);

    Task<Result<TikTokPublishStatusResponse>> GetPublishStatusAsync(
        string accessToken,
        string publishId,
        CancellationToken cancellationToken);
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

public sealed class TikTokPostInfo
{
    public string Title { get; set; } = string.Empty;
    public string PrivacyLevel { get; set; } = "SELF_ONLY";
    public bool DisableDuet { get; set; }
    public bool DisableComment { get; set; }
    public bool DisableStitch { get; set; }
    public int? VideoCoverTimestampMs { get; set; }
}

public sealed class TikTokVideoSourceInfo
{
    public string Source { get; set; } = "PULL_FROM_URL";
    public string? VideoUrl { get; set; }
    public long? VideoSize { get; set; }
    public int? ChunkSize { get; set; }
    public int? TotalChunkCount { get; set; }
}

public sealed class TikTokVideoInitResponse
{
    public string PublishId { get; set; } = string.Empty;
    public string? UploadUrl { get; set; }
}

public sealed class TikTokPublishStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public string? PublishedItemId { get; set; }
    public string? FailReason { get; set; }
}
