using Application.Abstractions.Data;
using Application.Abstractions.TikTok;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;
using System.Text.Json;

namespace Application.SocialMedias.Queries;

public sealed record GetSocialMediasByIdsQuery(
    Guid UserId,
    IReadOnlyList<Guid> SocialMediaIds) : IRequest<Result<IReadOnlyList<SocialMediaResponse>>>;

public sealed class GetSocialMediasByIdsQueryHandler
    : IRequestHandler<GetSocialMediasByIdsQuery, Result<IReadOnlyList<SocialMediaResponse>>>
{
    private const string TikTokType = "tiktok";
    private static readonly TimeSpan TikTokRefreshBuffer = TimeSpan.FromMinutes(5);
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly ITikTokOAuthService _tikTokOAuthService;

    public GetSocialMediasByIdsQueryHandler(IUnitOfWork unitOfWork, ITikTokOAuthService tikTokOAuthService)
    {
        _unitOfWork = unitOfWork;
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _tikTokOAuthService = tikTokOAuthService;
    }

    public async Task<Result<IReadOnlyList<SocialMediaResponse>>> Handle(
        GetSocialMediasByIdsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.SocialMediaIds.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("SocialMedia.Missing", "At least one social media id is required."));
        }

        var ids = request.SocialMediaIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("SocialMedia.Missing", "At least one social media id is required."));
        }

        var socialMedias = await _socialMediaRepository.GetAll()
            .Where(item => item.UserId == request.UserId &&
                           ids.Contains(item.Id) &&
                           !item.IsDeleted)
            .ToListAsync(cancellationToken);

        if (socialMedias.Count != ids.Count)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("SocialMedia.NotFound", "One or more social media accounts were not found."));
        }

        var hasUpdates = false;
        foreach (var socialMedia in socialMedias)
        {
            var refreshResult = await RefreshTikTokTokenIfNeededAsync(socialMedia, cancellationToken);
            if (refreshResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SocialMediaResponse>>(refreshResult.Error);
            }

            hasUpdates |= refreshResult.Value;
        }

        if (hasUpdates)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var response = socialMedias.Select(sm => SocialMediaMapping.ToResponse(sm, includeMetadata: true)).ToList();
        return Result.Success<IReadOnlyList<SocialMediaResponse>>(response);
    }

    private async Task<Result<bool>> RefreshTikTokTokenIfNeededAsync(
        SocialMedia socialMedia,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase) ||
            socialMedia.Metadata is null)
        {
            return Result.Success(false);
        }

        var root = socialMedia.Metadata.RootElement;
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var accessToken = GetString(root, "access_token");
        var refreshToken = GetString(root, "refresh_token");
        var expiresAt = GetDateTime(root, "expires_at");
        var refreshExpiresAt = GetDateTime(root, "refresh_expires_at");

        var needsRefresh = string.IsNullOrWhiteSpace(accessToken) ||
                           !expiresAt.HasValue ||
                           expiresAt.Value <= now.Add(TikTokRefreshBuffer);

        if (!needsRefresh)
        {
            return Result.Success(false);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Failure<bool>(
                new Error("TikTok.RefreshTokenMissing", "TikTok refresh token is missing. Please reconnect the account."));
        }

        if (refreshExpiresAt.HasValue && refreshExpiresAt.Value <= now)
        {
            return Result.Failure<bool>(
                new Error("TikTok.RefreshTokenExpired", "TikTok refresh token has expired. Please reconnect the account."));
        }

        var refreshResult = await _tikTokOAuthService.RefreshTokenAsync(refreshToken, cancellationToken);
        if (refreshResult.IsFailure)
        {
            return Result.Failure<bool>(refreshResult.Error);
        }

        TikTokUserProfile? profile = null;
        var profileResult = await _tikTokOAuthService.GetUserProfileAsync(
            refreshResult.Value.AccessToken,
            cancellationToken);
        if (profileResult.IsSuccess)
        {
            profile = profileResult.Value;
        }

        var refreshedMetadata = CreateRefreshedMetadata(root, refreshResult.Value, profile, now, refreshToken);
        socialMedia.Metadata?.Dispose();
        socialMedia.Metadata = refreshedMetadata;
        socialMedia.UpdatedAt = now;
        _socialMediaRepository.Update(socialMedia);

        return Result.Success(true);
    }

    private static JsonDocument CreateRefreshedMetadata(
        JsonElement root,
        TikTokTokenResponse tokenResponse,
        TikTokUserProfile? profile,
        DateTime now,
        string existingRefreshToken)
    {
        var payload = new
        {
            open_id = string.IsNullOrWhiteSpace(tokenResponse.OpenId) ? GetString(root, "open_id") : tokenResponse.OpenId,
            access_token = tokenResponse.AccessToken,
            refresh_token = string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) ? existingRefreshToken : tokenResponse.RefreshToken,
            expires_at = now.AddSeconds(tokenResponse.ExpiresIn),
            refresh_expires_at = now.AddSeconds(tokenResponse.RefreshExpiresIn),
            scope = string.IsNullOrWhiteSpace(tokenResponse.Scope) ? GetString(root, "scope") : tokenResponse.Scope,
            token_type = string.IsNullOrWhiteSpace(tokenResponse.TokenType) ? GetString(root, "token_type") : tokenResponse.TokenType,
            display_name = profile?.DisplayName ?? GetString(root, "display_name"),
            avatar_url = profile?.AvatarUrl ?? GetString(root, "avatar_url"),
            bio_description = profile?.BioDescription ?? GetString(root, "bio_description"),
            username = profile?.UnionId ?? GetString(root, "username"),
            follower_count = profile?.FollowerCount ?? GetInt(root, "follower_count"),
            following_count = profile?.FollowingCount ?? GetInt(root, "following_count")
        };

        return JsonDocument.Parse(JsonSerializer.Serialize(payload));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return element.GetString();
    }

    private static int? GetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return null;
    }

    private static DateTime? GetDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(
            element.GetString(),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
