using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Application.Abstractions.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Authentication;
using SharedLibrary.Common;

namespace Infrastructure.Logic.Security;

public sealed class RevocationAwareJwtTokenService : IJwtTokenService
{
    private readonly JwtTokenService _inner;
    private readonly IRepository<RefreshToken> _refreshTokenRepository;

    public RevocationAwareJwtTokenService(JwtTokenService inner, IUnitOfWork unitOfWork)
    {
        _inner = inner;
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
    }

    public string GenerateToken(Guid userId, string email, IEnumerable<string> roles) =>
        _inner.GenerateToken(userId, email, roles);

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var principal = _inner.ValidateToken(token);
        if (principal == null)
        {
            return null;
        }

        var jti = principal.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, JwtRegisteredClaimNames.Jti, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(claim.Type, "jti", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrWhiteSpace(jti))
        {
            return null;
        }

        var revoked = _refreshTokenRepository.GetAll()
            .AsNoTracking()
            .Where(rt => rt.AccessTokenJti == jti)
            .Select(rt => new { rt.RevokedAt, rt.AccessTokenRevokedAt })
            .FirstOrDefault();

        if (revoked != null && (revoked.RevokedAt != null || revoked.AccessTokenRevokedAt != null))
        {
            return null;
        }

        return principal;
    }

    public string GenerateRefreshToken() => _inner.GenerateRefreshToken();

    public bool IsTokenExpired(string token) => _inner.IsTokenExpired(token);
}

