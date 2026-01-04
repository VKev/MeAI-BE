using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record LogoutCommand(string? AccessToken, string? RefreshToken)
    : IRequest<Result<MessageResponse>>;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result<MessageResponse>>
{
    private readonly IRepository<RefreshToken> _refreshTokenRepository;

    public LogoutCommandHandler(IUnitOfWork unitOfWork)
    {
        _refreshTokenRepository = unitOfWork.Repository<RefreshToken>();
    }

    public async Task<Result<MessageResponse>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var revokedAt = DateTimeExtensions.PostgreSqlUtcNow;
        RefreshToken? refreshTokenEntity = null;

        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var tokenHash = HashToken(request.RefreshToken);
            refreshTokenEntity = await _refreshTokenRepository.GetAll()
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

            if (refreshTokenEntity != null)
            {
                if (refreshTokenEntity.RevokedAt == null)
                {
                    refreshTokenEntity.RevokedAt = revokedAt;
                }

                if (refreshTokenEntity.AccessTokenRevokedAt == null)
                {
                    refreshTokenEntity.AccessTokenRevokedAt = revokedAt;
                }

                _refreshTokenRepository.Update(refreshTokenEntity);
            }
        }

        var accessTokenJti = ExtractAccessTokenJti(request.AccessToken);
        if (!string.IsNullOrWhiteSpace(accessTokenJti))
        {
            if (refreshTokenEntity == null ||
                !string.Equals(refreshTokenEntity.AccessTokenJti, accessTokenJti, StringComparison.Ordinal))
            {
                var tokenByJti = await _refreshTokenRepository.GetAll()
                    .FirstOrDefaultAsync(rt => rt.AccessTokenJti == accessTokenJti, cancellationToken);

                if (tokenByJti != null)
                {
                    if (tokenByJti.RevokedAt == null)
                    {
                        tokenByJti.RevokedAt = revokedAt;
                    }

                    if (tokenByJti.AccessTokenRevokedAt == null)
                    {
                        tokenByJti.AccessTokenRevokedAt = revokedAt;
                    }

                    _refreshTokenRepository.Update(tokenByJti);
                }
            }
            else if (refreshTokenEntity.AccessTokenRevokedAt == null)
            {
                refreshTokenEntity.AccessTokenRevokedAt = revokedAt;
                _refreshTokenRepository.Update(refreshTokenEntity);
            }
        }

        return Result.Success(new MessageResponse("Logged out."));
    }

    private static string? ExtractAccessTokenJti(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var jwt = handler.ReadJwtToken(accessToken);
            var jti = jwt.Claims.FirstOrDefault(claim =>
                string.Equals(claim.Type, JwtRegisteredClaimNames.Jti, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, "jti", StringComparison.OrdinalIgnoreCase))?.Value;

            return string.IsNullOrWhiteSpace(jti) ? null : jti;
        }
        catch
        {
            return null;
        }
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
