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

namespace Application.Users.Commands.Refresh;

internal sealed class RefreshTokenCommandHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository,
    IRepository<RefreshToken> refreshTokenRepository,
    IJwtTokenService jwtTokenService,
    IConfiguration configuration)
    : ICommandHandler<RefreshTokenCommand, LoginResponse>
{
    private const int RefreshTokenDays = 7;
    private const int AccessTokenMinutesFallback = 60;
    private readonly int _accessTokenMinutes = int.TryParse(
        configuration["Jwt:ExpirationMinutes"], out var minutes)
        ? minutes
        : AccessTokenMinutesFallback;

    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var tokens = await refreshTokenRepository.FindAsync(
            token => token.TokenHash == tokenHash,
            cancellationToken);

        var tokenEntity = tokens.FirstOrDefault();
        if (tokenEntity == null || tokenEntity.RevokedAt != null ||
            tokenEntity.ExpiresAt <= DateTimeExtensions.PostgreSqlUtcNow)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Invalid or expired refresh token"));
        }

        var user = await userRepository.GetByIdAsync(tokenEntity.UserId, cancellationToken);
        if (user == null)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Invalid refresh token user"));
        }

        var roles = await ResolveRolesAsync(user.Id, roleRepository, userRoleRepository, cancellationToken);
        var accessToken = jwtTokenService.GenerateToken(user.Id, user.Email, roles);
        var newRefreshToken = await GenerateUniqueRefreshTokenAsync(
            jwtTokenService,
            refreshTokenRepository,
            cancellationToken);

        tokenEntity.RevokedAt = DateTimeExtensions.PostgreSqlUtcNow;
        tokenEntity.AccessTokenRevokedAt = DateTimeExtensions.PostgreSqlUtcNow;
        refreshTokenRepository.Update(tokenEntity);

        var newTokenEntity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = HashToken(newRefreshToken),
            AccessTokenJti = ExtractAccessTokenJti(accessToken),
            ExpiresAt = DateTimeExtensions.PostgreSqlUtcNow.AddDays(RefreshTokenDays),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await refreshTokenRepository.AddAsync(newTokenEntity, cancellationToken);

        var response = new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: newRefreshToken,
            ExpiresAt: DateTimeExtensions.PostgreSqlUtcNow.AddMinutes(_accessTokenMinutes),
            User: new UserInfo(
                user.Id,
                string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
                user.Email,
                roles));

        return Result.Success(response);
    }

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
}
