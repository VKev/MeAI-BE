using Application.Abstractions.Data;
using Application.Abstractions.Facebook;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;
using System.Text.Json;

namespace Application.SocialMedias.Queries;

public sealed record GetSocialMediasQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IRequest<Result<List<SocialMediaResponse>>>;

public sealed class GetSocialMediasQueryHandler
    : IRequestHandler<GetSocialMediasQuery, Result<List<SocialMediaResponse>>>
{
    private const string FacebookType = "facebook";
    private readonly IUnitOfWork _unitOfWork;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private readonly IRepository<SocialMedia> _repository;
    private readonly IFacebookOAuthService _facebookOAuthService;
    private readonly ISocialMediaProfileService _profileService;

    public GetSocialMediasQueryHandler(
        IUnitOfWork unitOfWork,
        ISocialMediaProfileService profileService,
        IFacebookOAuthService facebookOAuthService)
    {
        _unitOfWork = unitOfWork;
        _repository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
        _facebookOAuthService = facebookOAuthService;
    }

    public async Task<Result<List<SocialMediaResponse>>> Handle(GetSocialMediasQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = _repository.GetAll()
            .Where(sm => sm.UserId == request.UserId && !sm.IsDeleted);

        if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
        {
            var createdAt = request.CursorCreatedAt.Value;
            var lastId = request.CursorId.Value;
            query = query.Where(sm =>
                (sm.CreatedAt < createdAt) ||
                (sm.CreatedAt == createdAt && sm.Id.CompareTo(lastId) < 0));
        }

        var socialMedias = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var hasFacebookUpdates = await SyncFacebookPagesAsync(request.UserId, socialMedias, cancellationToken);
        if (hasFacebookUpdates)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var refreshedQuery = _repository.GetAll()
                .AsNoTracking()
                .Where(sm => sm.UserId == request.UserId && !sm.IsDeleted);

            if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
            {
                var createdAt = request.CursorCreatedAt.Value;
                var lastId = request.CursorId.Value;
                refreshedQuery = refreshedQuery.Where(sm =>
                    (sm.CreatedAt < createdAt) ||
                    (sm.CreatedAt == createdAt && sm.Id.CompareTo(lastId) < 0));
            }

            socialMedias = await refreshedQuery
                .OrderByDescending(sm => sm.CreatedAt)
                .ThenByDescending(sm => sm.Id)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        var responses = await Task.WhenAll(
            socialMedias.Select(async socialMedia =>
            {
                var profileResult = await _profileService.GetUserProfileAsync(
                    socialMedia.Type,
                    socialMedia.Metadata,
                    cancellationToken);

                var profile = profileResult.IsSuccess ? profileResult.Value : null;
                return SocialMediaMapping.ToResponse(socialMedia, profile);
            }));

        return Result.Success(responses.ToList());
    }

    private async Task<bool> SyncFacebookPagesAsync(
        Guid userId,
        IReadOnlyList<SocialMedia> currentPageSocialMedias,
        CancellationToken cancellationToken)
    {
        var facebookAccounts = currentPageSocialMedias
            .Where(item =>
                string.Equals(item.Type, FacebookType, StringComparison.OrdinalIgnoreCase) &&
                item.Metadata != null)
            .ToList();

        if (facebookAccounts.Count == 0)
        {
            return false;
        }

        var allFacebookAccounts = await _repository.GetAll()
            .Where(item =>
                item.UserId == userId &&
                item.Type == FacebookType &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);

        var metadataByAccessToken = facebookAccounts
            .Select(item => item.Metadata)
            .Where(metadata => TryGetMetadataValue(metadata, "access_token", out _))
            .GroupBy(metadata => GetMetadataString(metadata!, "access_token")!, StringComparer.Ordinal)
            .ToList();

        if (metadataByAccessToken.Count == 0)
        {
            return false;
        }

        var hasUpdates = false;
        foreach (var tokenGroup in metadataByAccessToken)
        {
            var accessToken = tokenGroup.Key;
            var representativeMetadata = tokenGroup.First();
            var metadataSeed = new FacebookMetadataSeed(
                AccessToken: accessToken,
                Email: GetMetadataString(representativeMetadata, "email"),
                ProfilePictureUrl: GetMetadataString(representativeMetadata, "profile_picture_url"),
                ExpiresAt: GetMetadataString(representativeMetadata, "expires_at"));
            var profileResult = await _facebookOAuthService.FetchProfileAsync(
                accessToken,
                cancellationToken,
                preferredPageId: null);

            if (profileResult.IsFailure || profileResult.Value.Pages.Count == 0)
            {
                continue;
            }

            foreach (var page in profileResult.Value.Pages)
            {
                var matchedSocialMedia = allFacebookAccounts.FirstOrDefault(item => MatchesFacebookPage(item.Metadata, page.Id));
                var metadata = CreateFacebookMetadata(profileResult.Value, page, metadataSeed);

                if (matchedSocialMedia != null)
                {
                    if (JsonEquals(matchedSocialMedia.Metadata, metadata))
                    {
                        metadata.Dispose();
                        continue;
                    }

                    matchedSocialMedia.Metadata?.Dispose();
                    matchedSocialMedia.Metadata = metadata;
                    matchedSocialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                    _repository.Update(matchedSocialMedia);
                    hasUpdates = true;
                    continue;
                }

                var socialMedia = new SocialMedia
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    Type = FacebookType,
                    Metadata = metadata,
                    CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
                };
                await _repository.AddAsync(socialMedia, cancellationToken);
                allFacebookAccounts.Add(socialMedia);
                hasUpdates = true;
            }
        }

        return hasUpdates;
    }

    private static JsonDocument CreateFacebookMetadata(
        FacebookProfileResponse profile,
        FacebookPageProfile page,
        FacebookMetadataSeed metadataSeed)
    {
        var payload = new Dictionary<string, object?>
        {
            ["provider"] = FacebookType,
            ["id"] = profile.Id,
            ["page_id"] = page.Id,
            ["page_name"] = page.Name,
            ["name"] = profile.Name,
            ["email"] = profile.Email ?? metadataSeed.Email,
            ["profile_picture_url"] = profile.ProfilePictureUrl ?? metadataSeed.ProfilePictureUrl,
            ["page_access_token"] = page.AccessToken,
            ["page_fan_count"] = page.FanCount,
            ["page_followers_count"] = page.FollowersCount,
            ["page_post_count"] = page.PostCount,
            ["access_token"] = metadataSeed.AccessToken
        };

        if (!string.IsNullOrWhiteSpace(metadataSeed.ExpiresAt))
        {
            payload["expires_at"] = metadataSeed.ExpiresAt;
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(payload));
    }

    private static bool MatchesFacebookPage(JsonDocument? metadata, string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return false;
        }

        return string.Equals(GetMetadataString(metadata, "page_id"), pageId, StringComparison.Ordinal) ||
               string.Equals(GetMetadataString(metadata, "id"), pageId, StringComparison.Ordinal);
    }

    private static bool JsonEquals(JsonDocument? left, JsonDocument right)
    {
        if (left == null)
        {
            return false;
        }

        return string.Equals(
            left.RootElement.GetRawText(),
            right.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    private static bool TryGetMetadataValue(JsonDocument? metadata, string propertyName, out string value)
    {
        value = string.Empty;
        var result = GetMetadataString(metadata, propertyName);
        if (string.IsNullOrWhiteSpace(result))
        {
            return false;
        }

        value = result;
        return true;
    }

    private static string? GetMetadataString(JsonDocument? metadata, string propertyName)
    {
        if (metadata == null ||
            !metadata.RootElement.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private sealed record FacebookMetadataSeed(
        string AccessToken,
        string? Email,
        string? ProfilePictureUrl,
        string? ExpiresAt);
}
