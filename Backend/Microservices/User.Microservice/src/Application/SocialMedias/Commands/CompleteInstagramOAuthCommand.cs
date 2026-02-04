using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Data;
using Application.Abstractions.Instagram;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CompleteInstagramOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteInstagramOAuthCommandHandler
    : IRequestHandler<CompleteInstagramOAuthCommand, Result<SocialMediaResponse>>
{
    private const string InstagramSocialMediaType = "instagram";

    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IInstagramOAuthService _instagramOAuthService;
    private readonly ISocialMediaProfileService _profileService;

    public CompleteInstagramOAuthCommandHandler(
        IUnitOfWork unitOfWork,
        IInstagramOAuthService instagramOAuthService,
        ISocialMediaProfileService profileService)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _instagramOAuthService = instagramOAuthService;
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        CompleteInstagramOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.AuthorizationDenied", request.ErrorDescription ?? request.Error));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.MissingCode", "Authorization code is missing"));
        }

        if (!_instagramOAuthService.TryValidateState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Instagram.InvalidState", "Invalid or expired state token"));
        }

        var tokenResult = await _instagramOAuthService.ExchangeCodeForTokenAsync(
            request.Code,
            cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var accessToken = tokenResult.Value.AccessToken;
        var expiresIn = tokenResult.Value.ExpiresIn;

        var debugResult = await _instagramOAuthService.ValidateTokenAsync(
            accessToken,
            cancellationToken);

        if (debugResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(debugResult.Error);
        }

        var profileResult = await _instagramOAuthService.FetchBusinessProfileAsync(
            accessToken,
            debugResult.Value,
            cancellationToken);

        if (profileResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(profileResult.Error);
        }

        var profile = profileResult.Value.Profile;

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        var resolvedUsername = !string.IsNullOrWhiteSpace(profile.Username)
            ? profile.Username
            : user?.Username;

        var resolvedEmail = user?.Email;

        var userInstagramAccounts = await _socialMediaRepository.GetAll()
            .Where(sm =>
                sm.UserId == userId &&
                sm.Type == InstagramSocialMediaType &&
                sm.Metadata != null &&
                !sm.IsDeleted)
            .ToListAsync(cancellationToken);

        var instagramAccountId = !string.IsNullOrWhiteSpace(profileResult.Value.InstagramAccountId)
            ? profileResult.Value.InstagramAccountId
            : profile.Id;

        SocialMedia? matchedSocialMedia = null;
        if (!string.IsNullOrWhiteSpace(instagramAccountId))
        {
            matchedSocialMedia = userInstagramAccounts.FirstOrDefault(sm =>
                MatchesInstagramAccount(sm.Metadata, instagramAccountId));
        }

        var payload = new Dictionary<string, object?>
        {
            ["provider"] = InstagramSocialMediaType,
            ["id"] = profile.Id,
            ["username"] = resolvedUsername,
            ["email"] = resolvedEmail,
            ["access_token"] = profileResult.Value.PageAccessToken,
            ["user_access_token"] = accessToken,
            ["token_type"] = tokenResult.Value.TokenType,
            ["user_id"] = profile.Id,
            ["page_id"] = profileResult.Value.PageId,
            ["page_name"] = profileResult.Value.PageName,
            ["instagram_business_account_id"] = profileResult.Value.InstagramAccountId,
            ["instagram_account_type"] = profileResult.Value.InstagramAccountType
        };

        if (expiresIn > 0)
        {
            payload["expires_at"] = now.AddSeconds(expiresIn);
        }

        var metadata = JsonDocument.Parse(JsonSerializer.Serialize(payload, MetadataJsonOptions));

        SocialMedia socialMedia;

        if (matchedSocialMedia != null)
        {
            matchedSocialMedia.Metadata?.Dispose();
            matchedSocialMedia.Metadata = metadata;
            matchedSocialMedia.UpdatedAt = now;
            socialMedia = matchedSocialMedia;
        }
        else
        {
            socialMedia = new SocialMedia
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Type = InstagramSocialMediaType,
                Metadata = metadata,
                CreatedAt = now
            };
            await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);
        }

        var socialProfileResult = await _profileService.GetUserProfileAsync(
            socialMedia.Type,
            socialMedia.Metadata,
            cancellationToken);

        var socialProfile = socialProfileResult.IsSuccess ? socialProfileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, socialProfile, includeMetadata: false));
    }

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static bool MatchesInstagramAccount(JsonDocument? metadata, string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        if (TryGetMetadataValue(metadata, "instagram_business_account_id", out var businessAccountId) &&
            string.Equals(businessAccountId, accountId, StringComparison.Ordinal))
        {
            return true;
        }

        if (TryGetMetadataValue(metadata, "id", out var id) &&
            string.Equals(id, accountId, StringComparison.Ordinal))
        {
            return true;
        }

        if (TryGetMetadataValue(metadata, "user_id", out var userId) &&
            string.Equals(userId, accountId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetMetadataValue(JsonDocument? metadata, string propertyName, out string value)
    {
        value = string.Empty;

        if (metadata == null)
        {
            return false;
        }

        if (metadata.RootElement.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
