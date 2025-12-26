using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.Register;

internal sealed class RegisterUserCommandHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository,
    IRepository<RefreshToken> refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IConfiguration configuration)
    : ICommandHandler<RegisterUserCommand, LoginResponse>
{
    private const int RefreshTokenDays = 7;
    private const int AccessTokenMinutesFallback = 60;
    private readonly int _accessTokenMinutes = int.TryParse(
        configuration["Jwt:ExpirationMinutes"], out var minutes)
        ? minutes
        : AccessTokenMinutesFallback;

    public async Task<Result<LoginResponse>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var normalizedUsername = NormalizeUsername(request.Username);

        var existingUsers = await userRepository.FindAsync(
            user => user.Email.ToLower() == normalizedEmail || user.Username.ToLower() == normalizedUsername,
            cancellationToken);

        if (existingUsers.Any(user => user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.EmailTaken", "Email is already registered"));
        }

        if (existingUsers.Any(user => user.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LoginResponse>(new Error("Auth.UsernameTaken", "Username is already taken"));
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Provider = "local",
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

        var roles = new List<string> { role.Name };
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
}
