using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Threads;

public interface IThreadsOAuthService
{
    (string AuthorizationUrl, string State) GenerateAuthorizationUrl(Guid userId, string? scopes);

    Task<Result<ThreadsTokenResponse>> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken);

    Task<Result<ThreadsTokenResponse>> RefreshTokenAsync(string accessToken, CancellationToken cancellationToken);

    bool TryValidateState(string state, out Guid userId);

    Task<Result<ThreadsUserProfile>> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed class ThreadsUserProfile
{
    public string? Id { get; set; }
    public string? Username { get; set; }
    public string? Name { get; set; }
    public string? ThreadsProfilePictureUrl { get; set; }
    public string? ThreadsBiography { get; set; }
}

public sealed class ThreadsTokenResponse
{
    public string UserId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "bearer";
}

public sealed class ThreadsErrorResponse
{
    public string ErrorType { get; set; } = string.Empty;
    public int Code { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
