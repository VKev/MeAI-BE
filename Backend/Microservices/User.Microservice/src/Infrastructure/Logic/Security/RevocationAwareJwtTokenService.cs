using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Application.Abstractions.Data;
using Application.Users;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Authentication;
using SharedLibrary.Common;

namespace Infrastructure.Logic.Security;

public sealed class RevocationAwareJwtTokenService : IJwtTokenService
{
    private readonly JwtTokenService _inner;
    private readonly IRepository<RefreshToken> _refreshTokenRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;

    public RevocationAwareJwtTokenService(JwtTokenService inner, IUnitOfWork unitOfWork)
    {
        _inner = inner;
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
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

        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        var userState = _userRepository.GetAll()
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsDeleted })
            .FirstOrDefault();

        if (userState == null || userState.IsDeleted)
        {
            return null;
        }

        var isBanned = (from userRole in _userRoleRepository.GetAll().AsNoTracking()
                        join role in _roleRepository.GetAll().AsNoTracking()
                            on userRole.RoleId equals role.Id
                        where userRole.UserId == userId &&
                              !userRole.IsDeleted &&
                              !role.IsDeleted &&
                              role.Name == UserAuthenticationRules.BannedRoleName
                        select userRole.Id)
            .Any();

        if (isBanned)
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

