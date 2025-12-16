using System.Security.Claims;

namespace SharedLibrary.Authentication;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string email, IEnumerable<string> roles);
    ClaimsPrincipal? ValidateToken(string token);
    string GenerateRefreshToken();
    bool IsTokenExpired(string token);
}
