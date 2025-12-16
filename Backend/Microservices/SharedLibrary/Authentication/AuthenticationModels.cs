namespace SharedLibrary.Authentication;

// Deprecated: use Application.Users.Commands.LoginUserCommand directly
// public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string AccessToken, 
    string RefreshToken, 
    DateTime ExpiresAt, 
    UserInfo User
);

public record UserInfo(
    Guid UserId, 
    string Name, 
    string Email, 
    IEnumerable<string> Roles
);

// Deprecated: use Application.Users.Commands.RefreshTokenCommand directly
// public record RefreshTokenRequest(string RefreshToken);

public record TokenValidationResult(
    bool IsValid,
    Guid? UserId = null,
    string? Email = null,
    IEnumerable<string>? Roles = null
);
