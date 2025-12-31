using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Authentication;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<LoginResponse>>;

public sealed class RefreshTokenCommandHandler
    : IRequestHandler<RefreshTokenCommand, Result<LoginResponse>>
{
    private const int RefreshTokenDays = 7;
    private const int AccessTokenMinutesFallback = 60;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IRepository<RefreshToken> _refreshTokenRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly int _accessTokenMinutes;

    public RefreshTokenCommandHandler(
        IUnitOfWork unitOfWork,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
        _jwtTokenService = jwtTokenService;
        _accessTokenMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var minutes)
            ? minutes
            : AccessTokenMinutesFallback;
    }

    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var tokenEntity = await _refreshTokenRepository.GetAll()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (tokenEntity == null || tokenEntity.RevokedAt != null ||
            tokenEntity.ExpiresAt <= DateTimeExtensions.PostgreSqlUtcNow)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Invalid or expired refresh token"));
        }

        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(u => u.Id == tokenEntity.UserId, cancellationToken);

        if (user == null)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Invalid refresh token user"));
        }

        var roles = await ResolveRolesAsync(user.Id, _roleRepository, _userRoleRepository, cancellationToken);
        var accessToken = _jwtTokenService.GenerateToken(user.Id, user.Email, roles);
        var newRefreshToken = await GenerateUniqueRefreshTokenAsync(
            _jwtTokenService,
            _refreshTokenRepository,
            cancellationToken);

        tokenEntity.RevokedAt = DateTimeExtensions.PostgreSqlUtcNow;
        tokenEntity.AccessTokenRevokedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _refreshTokenRepository.Update(tokenEntity);

        var newTokenEntity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = HashToken(newRefreshToken),
            AccessTokenJti = ExtractAccessTokenJti(accessToken),
            ExpiresAt = DateTimeExtensions.PostgreSqlUtcNow.AddDays(RefreshTokenDays),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _refreshTokenRepository.AddAsync(newTokenEntity, cancellationToken);

        var displayName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
        var response = new LoginResponse(
            accessToken,
            newRefreshToken,
            DateTimeExtensions.PostgreSqlUtcNow.AddMinutes(_accessTokenMinutes),
            user.Id,
            displayName,
            user.Email,
            roles);

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
            var exists = await refreshTokenRepository.GetAll()
                .AsNoTracking()
                .AnyAsync(rt => rt.TokenHash == hash, cancellationToken);
            if (!exists)
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
        var roleIds = await userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return new List<string> { "USER" };
        }

        var roleNames = await roleRepository.GetAll()
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);

        var roles = roleNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roles.Count == 0 ? new List<string> { "USER" } : roles;
    }
}
