namespace Application.Users.Models;

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Name,
    string Email,
    IEnumerable<string> Roles
);

public record TokenValidationResponse(
    bool IsValid,
    Guid? UserId = null,
    string? Email = null,
    IEnumerable<string>? Roles = null
);

public record MessageResponse(string Message);
