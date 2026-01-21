using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.TikTok;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record PublishTikTokVideoCommand(
    Guid UserId,
    Guid SocialMediaId,
    string Title,
    string PrivacyLevel,
    string VideoUrl,
    bool DisableDuet = false,
    bool DisableComment = false,
    bool DisableStitch = false,
    int? VideoCoverTimestampMs = null
) : IRequest<Result<TikTokPublishResponse>>;

public sealed record TikTokPublishResponse(
    string PublishId,
    string Status
);

public sealed class PublishTikTokVideoCommandHandler
    : IRequestHandler<PublishTikTokVideoCommand, Result<TikTokPublishResponse>>
{
    private readonly ITikTokOAuthService _tikTokService;
    private readonly IRepository<SocialMedia> _socialMediaRepository;

    public PublishTikTokVideoCommandHandler(
        ITikTokOAuthService tikTokService,
        IUnitOfWork unitOfWork)
    {
        _tikTokService = tikTokService;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<TikTokPublishResponse>> Handle(
        PublishTikTokVideoCommand request,
        CancellationToken cancellationToken)
    {
        // Get social media with access token
        var socialMedia = await _socialMediaRepository.GetAll()
            .FirstOrDefaultAsync(sm =>
                    sm.Id == request.SocialMediaId &&
                    sm.UserId == request.UserId &&
                    sm.Type == "tiktok" &&
                    !sm.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<TikTokPublishResponse>(
                new Error("TikTok.NotFound", "TikTok social media account not found"));
        }

        // Extract access token from metadata
        var accessToken = ExtractAccessToken(socialMedia.Metadata);
        if (string.IsNullOrEmpty(accessToken))
        {
            return Result.Failure<TikTokPublishResponse>(
                new Error("TikTok.InvalidToken", "Access token not found in social media metadata"));
        }

        // Check token expiry and refresh if needed
        var expiresAt = ExtractExpiresAt(socialMedia.Metadata);
        if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
        {
            var refreshToken = ExtractRefreshToken(socialMedia.Metadata);
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Result.Failure<TikTokPublishResponse>(
                    new Error("TikTok.TokenExpired", "Access token expired and no refresh token available"));
            }

            var refreshResult = await _tikTokService.RefreshTokenAsync(refreshToken, cancellationToken);
            if (refreshResult.IsFailure)
            {
                return Result.Failure<TikTokPublishResponse>(refreshResult.Error);
            }

            accessToken = refreshResult.Value.AccessToken;
        }

        // Create post info
        var postInfo = new TikTokPostInfo
        {
            Title = request.Title,
            PrivacyLevel = request.PrivacyLevel,
            DisableDuet = request.DisableDuet,
            DisableComment = request.DisableComment,
            DisableStitch = request.DisableStitch,
            VideoCoverTimestampMs = request.VideoCoverTimestampMs
        };

        // Create source info for PULL_FROM_URL
        var sourceInfo = new TikTokVideoSourceInfo
        {
            Source = "PULL_FROM_URL",
            VideoUrl = request.VideoUrl
        };

        // Initiate video publish
        var publishResult = await _tikTokService.InitiateVideoPublishAsync(
            accessToken,
            postInfo,
            sourceInfo,
            cancellationToken);

        if (publishResult.IsFailure)
        {
            return Result.Failure<TikTokPublishResponse>(publishResult.Error);
        }

        return Result.Success(new TikTokPublishResponse(
            publishResult.Value.PublishId,
            "PROCESSING"
        ));
    }

    private static string? ExtractAccessToken(JsonDocument? metadata)
    {
        if (metadata == null)
            return null;

        if (metadata.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            return accessTokenElement.GetString();
        }

        return null;
    }

    private static string? ExtractRefreshToken(JsonDocument? metadata)
    {
        if (metadata == null)
            return null;

        if (metadata.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement))
        {
            return refreshTokenElement.GetString();
        }

        return null;
    }

    private static DateTime? ExtractExpiresAt(JsonDocument? metadata)
    {
        if (metadata == null)
            return null;

        if (metadata.RootElement.TryGetProperty("expires_at", out var expiresAtElement))
        {
            if (expiresAtElement.TryGetDateTime(out var expiresAt))
            {
                return expiresAt;
            }
        }

        return null;
    }
}
