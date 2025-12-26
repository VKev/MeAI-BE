using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Domain.Entities;
using Domain.Repositories;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.Login;

internal sealed class LoginWithGoogleCommandHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository,
    IRepository<RefreshToken> refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IConfiguration configuration)
    : ICommandHandler<LoginWithGoogleCommand, LoginResponse>
{
    private const int RefreshTokenDays = 7;
    private const int AccessTokenMinutesFallback = 60;
    private readonly int _accessTokenMinutes = int.TryParse(
        configuration["Jwt:ExpirationMinutes"], out var minutes)
        ? minutes
        : AccessTokenMinutesFallback;

    private readonly string? _googleClientId = configuration["Google:ClientId"];

    public async Task<Result<LoginResponse>> Handle(LoginWithGoogleCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.InvalidGoogleToken", "Missing Google id token"));
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(_googleClientId))
            {
                settings.Audience = new[] { _googleClientId };
            }

            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
        }
        catch (Exception)
        {
            return Result.Failure<LoginResponse>(new Error("Auth.InvalidGoogleToken", "Invalid Google token"));
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.InvalidGoogleToken", "Google account email missing"));
        }

        var normalizedEmail = NormalizeEmail(payload.Email);
        var users = await userRepository.FindAsync(user => user.Email.ToLower() == normalizedEmail, cancellationToken);
        var user = users.FirstOrDefault();
        List<string> roles;

        if (user == null)
        {
            var username = await GenerateUniqueUsernameAsync(payload, userRepository, cancellationToken);

            user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = normalizedEmail,
                PasswordHash = passwordHasher.HashPassword(Guid.NewGuid().ToString("N")),
                FullName = string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name.Trim(),
                Provider = "google",
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };

            await userRepository.AddAsync(user, cancellationToken);

            var role = await GetOrCreateDefaultRole(roleRepository, cancellationToken);
            var userRole = new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = role.Id,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };

            await userRoleRepository.AddAsync(userRole, cancellationToken);
            roles = new List<string> { role.Name };
        }
        else
        {
            if (!string.Equals(user.Provider, "google", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<LoginResponse>(
                    new Error("Auth.ProviderMismatch", "Account is not linked with Google"));
            }

            roles = await ResolveRolesAsync(user.Id, roleRepository, userRoleRepository, cancellationToken);
        }

        var accessToken = jwtTokenService.GenerateToken(user.Id, user.Email, roles);
        var refreshToken = await GenerateUniqueRefreshTokenAsync(
            jwtTokenService,
            refreshTokenRepository,
            cancellationToken);

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            AccessTokenJti = ExtractAccessTokenJti(accessToken),
            ExpiresAt = DateTimeExtensions.PostgreSqlUtcNow.AddDays(RefreshTokenDays),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);

        var response = new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: DateTimeExtensions.PostgreSqlUtcNow.AddMinutes(_accessTokenMinutes),
            User: new UserInfo(
                user.Id,
                string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
                user.Email,
                roles));

        return Result.Success(response);
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static string ExtractAccessTokenJti(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);
        var jti = jwt.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, JwtRegisteredClaimNames.Jti, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(claim.Type, "jti", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrWhiteSpace(jti))
        {
            throw new InvalidOperationException("Access token missing jti claim.");
        }

        return jti;
    }

    private static async Task<string> GenerateUniqueRefreshTokenAsync(
        IJwtTokenService jwtTokenService,
        IRepository<RefreshToken> refreshTokenRepository,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var token = jwtTokenService.GenerateRefreshToken();
            var hash = HashToken(token);
            var existing = await refreshTokenRepository.FindAsync(
                rt => rt.TokenHash == hash,
                cancellationToken);
            if (existing.Count == 0)
            {
                return token;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique refresh token.");
    }

    private static async Task<List<string>> ResolveRolesAsync(
        Guid userId,
        IRepository<Role> roleRepository,
        IRepository<UserRole> userRoleRepository,
        CancellationToken cancellationToken)
    {
        var userRoles = await userRoleRepository.FindAsync(ur => ur.UserId == userId, cancellationToken);
        if (userRoles.Count == 0)
        {
            return new List<string> { "USER" };
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await roleRepository.FindAsync(role => roleIds.Contains(role.Id), cancellationToken);
        var roleNames = roles.Select(role => role.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        return roleNames.Count == 0 ? new List<string> { "USER" } : roleNames;
    }

    private static async Task<Role> GetOrCreateDefaultRole(
        IRepository<Role> roleRepository,
        CancellationToken cancellationToken)
    {
        var roles = await roleRepository.FindAsync(role => role.Name == "USER", cancellationToken);
        var role = roles.FirstOrDefault();
        if (role != null) return role;

        role = new Role
        {
            Id = Guid.NewGuid(),
            Name = "USER",
            Description = "Standard user",
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await roleRepository.AddAsync(role, cancellationToken);
        return role;
    }

    private static async Task<string> GenerateUniqueUsernameAsync(
        GoogleJsonWebSignature.Payload payload,
        IRepository<User> userRepository,
        CancellationToken cancellationToken)
    {
        var baseName = payload.Email?.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                       ?? "google_user";
        baseName = baseName.Replace(".", "_").Replace("-", "_");
        baseName = NormalizeUsername(baseName);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = attempt == 0 ? baseName : $"{baseName}_{attempt}";
            var existing = await userRepository.FindAsync(
                u => u.Username.ToLower() == candidate,
                cancellationToken);
            if (existing.Count == 0)
            {
                return candidate;
            }
        }

        var fallback = $"{baseName}_{Guid.NewGuid():N}";
        return fallback.Length > 30 ? fallback[..30] : fallback;
    }
}
