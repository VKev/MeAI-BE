using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Facebook;
using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.Subscriptions.Services;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record CompleteFacebookOAuthCommand(
    string Code,
    string State,
    string? Error,
    string? ErrorDescription) : IRequest<Result<SocialMediaResponse>>;

public sealed class CompleteFacebookOAuthCommandHandler
    : IRequestHandler<CompleteFacebookOAuthCommand, Result<SocialMediaResponse>>
{
    private const string FacebookSocialMediaType = "facebook";

    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IFacebookOAuthService _facebookOAuthService;
    private readonly IUserSubscriptionEntitlementService _userSubscriptionEntitlementService;
    private readonly ISocialMediaProfileService _profileService;

    public CompleteFacebookOAuthCommandHandler(
        IUnitOfWork unitOfWork,
        IFacebookOAuthService facebookOAuthService,
        IUserSubscriptionEntitlementService userSubscriptionEntitlementService,
        ISocialMediaProfileService profileService)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _userRepository = unitOfWork.Repository<User>();
        _facebookOAuthService = facebookOAuthService;
        _userSubscriptionEntitlementService = userSubscriptionEntitlementService;
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(
        CompleteFacebookOAuthCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Error))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.AuthorizationDenied", request.ErrorDescription ?? request.Error));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.MissingCode", "Authorization code is missing"));
        }

        if (!_facebookOAuthService.TryValidateState(request.State, out var userId))
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.InvalidState", "Invalid or expired state token"));
        }

        var tokenResult = await _facebookOAuthService.ExchangeCodeForTokenAsync(
            request.Code,
            cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(tokenResult.Error);
        }

        var debugResult = await _facebookOAuthService.ValidateTokenAsync(
            tokenResult.Value.AccessToken,
            cancellationToken);

        if (debugResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(debugResult.Error);
        }

        var profileResult = await _facebookOAuthService.FetchProfileAsync(
            tokenResult.Value.AccessToken,
            cancellationToken);

        if (profileResult.IsFailure)
        {
            return Result.Failure<SocialMediaResponse>(profileResult.Error);
        }

        var profile = profileResult.Value;
        var accountCandidates = ResolveAccountCandidates(profile);

        if (accountCandidates.Count == 0)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("Facebook.PageMissing", "No Facebook page is available for this account."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("User.NotFound", "User not found"));
        }

        // Include soft-deleted rows so reconnecting the same external account revives the
        // original row in place. Hard-deleting on disconnect would orphan every post that
        // references its SocialMediaId; revive-on-reconnect keeps the history intact.
        var userFacebookAccounts = await _socialMediaRepository.GetAll()
            .Where(sm =>
                sm.UserId == userId &&
                sm.Type == FacebookSocialMediaType &&
                sm.Metadata != null)
            .ToListAsync(cancellationToken);

        var matchedAccounts = accountCandidates.ToDictionary(
            candidate => candidate.Identifier,
            candidate => userFacebookAccounts.FirstOrDefault(sm => MatchesFacebookAccount(sm.Metadata, candidate.Identifier)));

        var newAccountCount = matchedAccounts.Values.Count(socialMedia => socialMedia == null);
        if (newAccountCount > 0)
        {
            var entitlementResult = await EnsureSocialAccountLinkAllowedAsync(
                userId,
                newAccountCount,
                cancellationToken);

            if (entitlementResult.IsFailure)
            {
                return Result.Failure<SocialMediaResponse>(entitlementResult.Error);
            }
        }

        var shouldUpdateUser = matchedAccounts.Values.Any(socialMedia => socialMedia != null) || userFacebookAccounts.Count == 0;

        var resolvedEmail = user.Email;
        var resolvedName = user.FullName ?? user.Username;
        var userUpdated = false;

        if (!string.IsNullOrWhiteSpace(profile.Name))
        {
            var normalizedName = profile.Name.Trim();
            if (shouldUpdateUser && !string.Equals(user.FullName, normalizedName, StringComparison.Ordinal))
            {
                user.FullName = normalizedName;
                userUpdated = true;
            }

            resolvedName = normalizedName;
        }

        if (!string.IsNullOrWhiteSpace(profile.Email))
        {
            // Resolve the FB profile email for downstream use (Stripe, notifications,
            // social_medias.metadata) but do NOT overwrite `user.Email`. The user's
            // primary email is their auth identity — silently changing it on social
            // link breaks future logins and password resets. The FB email is already
            // captured in social_medias.metadata; nothing else needs it on the user row.
            resolvedEmail = NormalizeEmail(profile.Email);
        }

        if (shouldUpdateUser && userUpdated)
        {
            user.UpdatedAt = now;
            _userRepository.Update(user);
        }

        var persistedSocialMedias = new List<SocialMedia>(accountCandidates.Count);

        foreach (var candidate in accountCandidates)
        {
            var metadata = CreateMetadataDocument(
                profile,
                candidate,
                resolvedName,
                resolvedEmail,
                tokenResult.Value.AccessToken,
                tokenResult.Value.ExpiresIn,
                now);

            if (matchedAccounts[candidate.Identifier] is { } matchedSocialMedia)
            {
                matchedSocialMedia.Metadata?.Dispose();
                matchedSocialMedia.Metadata = metadata;
                matchedSocialMedia.UpdatedAt = now;
                // Revive if the row was soft-deleted — this is the reconnect path. Clear
                // the tombstone so queries that filter on DeletedAt surface the row again.
                if (matchedSocialMedia.IsDeleted)
                {
                    matchedSocialMedia.IsDeleted = false;
                    matchedSocialMedia.DeletedAt = null;
                }
                persistedSocialMedias.Add(matchedSocialMedia);
                continue;
            }

            var socialMedia = new SocialMedia
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Type = FacebookSocialMediaType,
                Metadata = metadata,
                CreatedAt = now
            };
            await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);
            persistedSocialMedias.Add(socialMedia);
        }

        var primarySocialMedia = persistedSocialMedias[0];

        var socialProfileResult = await _profileService.GetUserProfileAsync(
            primarySocialMedia.Type,
            primarySocialMedia.Metadata,
            cancellationToken);

        var socialProfile = socialProfileResult.IsSuccess ? socialProfileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(primarySocialMedia, socialProfile, includeMetadata: false));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

    private async Task<Result<UserSubscriptionEntitlement>> EnsureSocialAccountLinkAllowedAsync(
        Guid userId,
        int newPageCount,
        CancellationToken cancellationToken)
    {
        var entitlement = await _userSubscriptionEntitlementService.GetCurrentEntitlementAsync(userId, cancellationToken);

        if (entitlement.MaxSocialAccounts <= 0)
        {
            return Result.Failure<UserSubscriptionEntitlement>(
                new Error("SocialMedia.LimitUnavailable", "Your current plan does not include social account linking."));
        }

        var existingFacebookCount = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .CountAsync(item => item.UserId == userId && item.Type == FacebookSocialMediaType && !item.IsDeleted, cancellationToken);

        var isNewConnection = existingFacebookCount == 0;

        if (isNewConnection)
        {
            var currentAccountCount = await _socialMediaRepository.GetAll()
                .AsNoTracking()
                .Where(item => item.UserId == userId && !item.IsDeleted)
                .Select(item => item.Type)
                .Distinct()
                .CountAsync(cancellationToken);

            if (currentAccountCount + 1 > entitlement.MaxSocialAccounts)
            {
                return Result.Failure<UserSubscriptionEntitlement>(
                    new Error(
                        "SocialMedia.LimitExceeded",
                        $"Your current plan allows up to {entitlement.MaxSocialAccounts} linked social account(s). Upgrade to add more."));
            }
        }

        var totalPages = existingFacebookCount + newPageCount;
        if (totalPages > entitlement.MaxPagesPerSocialAccount)
        {
            return Result.Failure<UserSubscriptionEntitlement>(
                new Error(
                    "SocialMedia.PageLimitExceeded",
                    $"Your current plan allows up to {entitlement.MaxPagesPerSocialAccount} page(s) per social account. Upgrade to add more."));
        }

        return Result.Success(entitlement);
    }

    private static JsonDocument CreateMetadataDocument(
        FacebookProfileResponse profile,
        FacebookAccountCandidate candidate,
        string? resolvedName,
        string? resolvedEmail,
        string accessToken,
        int expiresIn,
        DateTime now)
    {
        var payload = new Dictionary<string, object?>
        {
            ["provider"] = FacebookSocialMediaType,
            ["id"] = profile.Id,
            ["page_id"] = candidate.PageId,
            ["page_name"] = candidate.PageName,
            ["name"] = resolvedName,
            ["email"] = resolvedEmail,
            ["profile_picture_url"] = profile.ProfilePictureUrl,
            ["page_access_token"] = candidate.PageAccessToken,
            ["page_fan_count"] = candidate.PageLikeCount,
            ["page_followers_count"] = candidate.PageFollowerCount,
            ["page_post_count"] = candidate.PagePostCount,
            ["access_token"] = accessToken
        };

        if (expiresIn > 0)
        {
            payload["expires_at"] = now.AddSeconds(expiresIn);
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(payload, MetadataJsonOptions));
    }

    private static List<FacebookAccountCandidate> ResolveAccountCandidates(FacebookProfileResponse profile)
    {
        if (profile.Pages.Count > 0)
        {
            return profile.Pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Id))
                .Select(page => new FacebookAccountCandidate(
                    page.Id,
                    page.Id,
                    page.Name,
                    page.AccessToken,
                    page.FanCount,
                    page.FollowersCount,
                    page.PostCount))
                .ToList();
        }

        var identifier = profile.PageId ?? profile.Id;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return [];
        }

        return
        [
            new FacebookAccountCandidate(
                identifier,
                profile.PageId,
                profile.PageName ?? profile.Name,
                profile.PageAccessToken,
                profile.PageLikeCount,
                profile.PageFollowerCount,
                profile.PagePostCount)
        ];
    }

    private static bool MatchesFacebookAccount(JsonDocument? metadata, string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        return (TryGetMetadataValue(metadata, "page_id", out var existingPageId) &&
                string.Equals(existingPageId, identifier, StringComparison.Ordinal)) ||
               (TryGetMetadataValue(metadata, "id", out var existingId) &&
                string.Equals(existingId, identifier, StringComparison.Ordinal));
    }

    private sealed record FacebookAccountCandidate(
        string Identifier,
        string? PageId,
        string? PageName,
        string? PageAccessToken,
        int? PageLikeCount,
        int? PageFollowerCount,
        int? PagePostCount);

}
