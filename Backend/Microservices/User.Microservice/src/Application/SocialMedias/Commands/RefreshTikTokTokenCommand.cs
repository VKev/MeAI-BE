using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.Abstractions.TikTok;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record RefreshTikTokTokenCommand(
    Guid SocialMediaId,
    Guid UserId) : IRequest<Result<SocialMediaResponse>>;

public sealed class RefreshTikTokTokenCommandHandler
    : IRequestHandler<RefreshTikTokTokenCommand, Result<SocialMediaResponse>>
{
    private readonly ITikTokOAuthService _tikTokOAuthService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly ISocialMediaProfileService _profileService;

    public RefreshTikTokTokenCommandHandler(
        ITikTokOAuthService tikTokOAuthService,
        IUnitOfWork unitOfWork,
        ISocialMediaProfileService profileService)
    {
        _tikTokOAuthService = tikTokOAuthService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        RefreshTikTokTokenCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.Id == request.SocialMediaId &&
                    sm.UserId == request.UserId &&
                    sm.Type == "tiktok" &&
                    !sm.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("TikTok.NotFound", "TikTok social media connection not found"));
        }

        if (socialMedia.Metadata == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("TikTok.NoMetadata", "No token metadata found"));
        }

        var refreshToken = socialMedia.Metadata.RootElement.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString()
            : null;

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("TikTok.NoRefreshToken", "Refresh token not found"));
        }

        var tokenResult = await _tikTokOAuthService.RefreshTokenAsync(refreshToken, cancellationToken);
        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var tokenResponse = tokenResult.Value;
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var newMetadata = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            open_id = tokenResponse.OpenId,
            access_token = tokenResponse.AccessToken,
            refresh_token = tokenResponse.RefreshToken,
            expires_at = now.AddSeconds(tokenResponse.ExpiresIn),
            refresh_expires_at = now.AddSeconds(tokenResponse.RefreshExpiresIn),
            scope = tokenResponse.Scope,
            token_type = tokenResponse.TokenType
        }));

        socialMedia.Metadata?.Dispose();
        socialMedia.Metadata = newMetadata;
        socialMedia.UpdatedAt = now;

        var profileResult = await _profileService.GetUserProfileAsync(
            socialMedia.Type,
            socialMedia.Metadata,
            cancellationToken);

        var profile = profileResult.IsSuccess ? profileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, profile));
    }
}
