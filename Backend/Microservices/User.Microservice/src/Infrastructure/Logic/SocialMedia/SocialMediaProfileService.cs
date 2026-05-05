using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.SocialMedia;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.SocialMedia;

public sealed class SocialMediaProfileService : ISocialMediaProfileService
{
    private readonly ITikTokOAuthService _tikTokOAuthService;
    private readonly IThreadsOAuthService _threadsOAuthService;
    private readonly IFacebookOAuthService _facebookOAuthService;
    private readonly IInstagramOAuthService _instagramOAuthService;
    private readonly ILogger<SocialMediaProfileService> _logger;

    public SocialMediaProfileService(
        ITikTokOAuthService tikTokOAuthService,
        IThreadsOAuthService threadsOAuthService,
        IFacebookOAuthService facebookOAuthService,
        IInstagramOAuthService instagramOAuthService,
        ILogger<SocialMediaProfileService> logger)
    {
        _tikTokOAuthService = tikTokOAuthService;
        _threadsOAuthService = threadsOAuthService;
        _facebookOAuthService = facebookOAuthService;
        _instagramOAuthService = instagramOAuthService;
        _logger = logger;
    }

    public async Task<Result<SocialMediaUserProfile>> GetUserProfileAsync(
        string type,
        JsonDocument? metadata,
        CancellationToken cancellationToken)
    {
        if (metadata == null)
        {
            return Result.Failure<SocialMediaUserProfile>(
                new Error("SocialMedia.NoMetadata", "No metadata available to fetch profile"));
        }

        if (!TryGetAccessToken(metadata, "access_token", out var accessToken))
        {
            return Result.Failure<SocialMediaUserProfile>(
                new Error("SocialMedia.NoAccessToken", "Access token not found in metadata"));
        }

        return type.ToLowerInvariant() switch
        {
            "tiktok" => await GetTikTokProfileAsync(accessToken, cancellationToken),
            "threads" => await GetThreadsProfileAsync(metadata, accessToken, cancellationToken),
            "facebook" => await GetFacebookProfileAsync(metadata, accessToken, cancellationToken),
            "instagram" => await GetInstagramProfileAsync(metadata, accessToken, cancellationToken),
            _ => Result.Failure<SocialMediaUserProfile>(
                new Error("SocialMedia.UnsupportedType", $"Profile fetching not supported for type: {type}"))
        };
    }

    private async Task<Result<SocialMediaUserProfile>> GetTikTokProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = await _tikTokOAuthService.GetUserProfileAsync(accessToken, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<SocialMediaUserProfile>(result.Error);
        }

        var profile = result.Value;
        return Result.Success(new SocialMediaUserProfile(
            UserId: profile.OpenId,
            Username: profile.UnionId,
            DisplayName: profile.DisplayName,
            ProfilePictureUrl: profile.AvatarUrl,
            Bio: profile.BioDescription,
            FollowerCount: profile.FollowerCount,
            FollowingCount: profile.FollowingCount,
            PostCount: null,
            PageLikeCount: null));
    }

    private async Task<Result<SocialMediaUserProfile>> GetThreadsProfileAsync(
        JsonDocument metadata,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var metadataProfileResult = GetThreadsProfileFromMetadata(metadata);
        var metadataProfile = metadataProfileResult.IsSuccess ? metadataProfileResult.Value : null;

        var result = await _threadsOAuthService.GetUserProfileAsync(accessToken, cancellationToken);

        // If profile fetch fails, try refreshing the token and retrying
        if (result.IsFailure)
        {
            _logger.LogWarning("Threads profile fetch failed: {Error}. Attempting token refresh.", result.Error.Description);

            var refreshResult = await _threadsOAuthService.RefreshTokenAsync(accessToken, cancellationToken);
            if (refreshResult.IsSuccess)
            {
                _logger.LogInformation("Threads token refreshed successfully. Retrying profile fetch.");
                result = await _threadsOAuthService.GetUserProfileAsync(refreshResult.Value.AccessToken, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Threads token refresh also failed: {Error}", refreshResult.Error.Description);
            }
        }

        if (result.IsFailure)
        {
            _logger.LogWarning("Threads profile fetch failed after retry. Falling back to metadata. MetadataProfile: Username={Username}, DisplayName={DisplayName}",
                metadataProfile?.Username, metadataProfile?.DisplayName);
            return metadataProfileResult;
        }

        var profile = result.Value;
        _logger.LogInformation("Threads profile fetched: Id={Id}, Username={Username}, Name={Name}, HasPicture={HasPicture}",
            profile.Id, profile.Username, profile.Name, profile.ThreadsProfilePictureUrl != null);

        return Result.Success(new SocialMediaUserProfile(
            UserId: FirstNonEmpty(profile.Id, metadataProfile?.UserId),
            Username: FirstNonEmpty(profile.Username, metadataProfile?.Username),
            DisplayName: FirstNonEmpty(profile.Name, profile.Username, metadataProfile?.DisplayName, metadataProfile?.Username),
            ProfilePictureUrl: FirstNonEmpty(
                profile.ThreadsProfilePictureUrl,
                metadataProfile?.ProfilePictureUrl,
                GetString(metadata.RootElement, "profile_picture_url")),
            Bio: FirstNonEmpty(
                profile.ThreadsBiography,
                metadataProfile?.Bio,
                GetString(metadata.RootElement, "biography")),
            FollowerCount: profile.FollowersCount ?? metadataProfile?.FollowerCount,
            FollowingCount: profile.FollowsCount ?? metadataProfile?.FollowingCount,
            PostCount: profile.MediaCount ?? metadataProfile?.PostCount,
            PageLikeCount: null));
    }

    private async Task<Result<SocialMediaUserProfile>> GetFacebookProfileAsync(
        JsonDocument metadata,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var preferredPageId = GetString(metadata.RootElement, "page_id");
        var result = await _facebookOAuthService.FetchProfileAsync(
            accessToken,
            cancellationToken,
            preferredPageId);

        if (result.IsFailure)
        {
            return GetFacebookProfileFromMetadata(metadata);
        }

        var profile = result.Value;
        // Prefer the picture URL we already fetched explicitly (CDN URL with token);
        // fall back to the unauthenticated Graph redirect endpoint when the explicit
        // picture call wasn't included for this page.
        var pageProfilePictureUrl = profile.PageProfilePictureUrl
                                    ?? (!string.IsNullOrWhiteSpace(profile.PageId)
                                        ? $"https://graph.facebook.com/v24.0/{profile.PageId}/picture?type=large"
                                        : null);

        // For a Facebook page connection, the user-visible "username" and "bio" are
        // really the page's vanity handle and About text — those are what any frontend
        // would want to render. Surface them here instead of forcing every consumer to
        // dig through metadataJson to find them.
        return Result.Success(new SocialMediaUserProfile(
            UserId: profile.Id,
            Username: profile.PageUsername,
            DisplayName: profile.Name,
            ProfilePictureUrl: profile.ProfilePictureUrl,
            Bio: FirstNonEmpty(profile.PageDescription, profile.PageAbout, profile.PageBio),
            FollowerCount: profile.PageFollowerCount,
            FollowingCount: null,  // FB pages don't follow other accounts
            PostCount: profile.PagePostCount,
            PageLikeCount: profile.PageLikeCount,
            PageId: profile.PageId,
            PageName: profile.PageName,
            PageProfilePictureUrl: pageProfilePictureUrl));
    }

    private async Task<Result<SocialMediaUserProfile>> GetInstagramProfileAsync(
        JsonDocument metadata,
        string accessToken,
        CancellationToken cancellationToken)
    {
        // Instagram requires debug token for fetching business profile
        var debugResult = await _instagramOAuthService.ValidateTokenAsync(accessToken, cancellationToken);

        if (debugResult.IsFailure)
        {
            // Fall back to basic info from metadata if available
            return GetInstagramProfileFromMetadata(metadata);
        }

        var userAccessToken = TryGetAccessToken(metadata, "user_access_token", out var userToken)
            ? userToken
            : accessToken;

        var result = await _instagramOAuthService.FetchBusinessProfileAsync(
            userAccessToken,
            debugResult.Value,
            cancellationToken);

        if (result.IsFailure)
        {
            // Fall back to basic info from metadata
            return GetInstagramProfileFromMetadata(metadata);
        }

        var profile = result.Value;
        return Result.Success(new SocialMediaUserProfile(
            UserId: profile.Profile.Id,
            Username: profile.Profile.Username,
            DisplayName: profile.Profile.Name ?? profile.PageName,
            ProfilePictureUrl: profile.Profile.ProfilePictureUrl,
            Bio: profile.Profile.Biography,
            FollowerCount: profile.Profile.FollowersCount,
            FollowingCount: profile.Profile.FollowsCount,
            PostCount: profile.Profile.MediaCount,
            PageLikeCount: null));
    }

    private static Result<SocialMediaUserProfile> GetInstagramProfileFromMetadata(JsonDocument metadata)
    {
        var root = metadata.RootElement;

        var userId = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var username = root.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() : null;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var pageName = root.TryGetProperty("page_name", out var pageNameElement) ? pageNameElement.GetString() : null;
        var profilePictureUrl = root.TryGetProperty("profile_picture_url", out var profilePictureElement)
            ? profilePictureElement.GetString()
            : null;
        var biography = root.TryGetProperty("biography", out var biographyElement)
            ? biographyElement.GetString()
            : null;
        var followerCount = TryGetIntValue(root, "followers_count");
        var followingCount = TryGetIntValue(root, "follows_count");
        var postCount = TryGetIntValue(root, "media_count");

        return Result.Success(new SocialMediaUserProfile(
            UserId: userId,
            Username: username,
            DisplayName: name ?? pageName,
            ProfilePictureUrl: profilePictureUrl,
            Bio: biography,
            FollowerCount: followerCount,
            FollowingCount: followingCount,
            PostCount: postCount,
            PageLikeCount: null));
    }

    private static Result<SocialMediaUserProfile> GetFacebookProfileFromMetadata(JsonDocument metadata)
    {
        var root = metadata.RootElement;

        var pageId = GetString(root, "page_id");
        var pageProfilePictureUrl = !string.IsNullOrWhiteSpace(pageId)
            ? $"https://graph.facebook.com/v24.0/{pageId}/picture?type=large"
            : null;

        return Result.Success(new SocialMediaUserProfile(
            UserId: GetString(root, "id"),
            Username: null,
            DisplayName: GetString(root, "name"),
            ProfilePictureUrl: GetString(root, "profile_picture_url"),
            Bio: null,
            FollowerCount: TryGetIntValue(root, "page_followers_count"),
            FollowingCount: null,
            PostCount: TryGetIntValue(root, "page_post_count"),
            PageLikeCount: TryGetIntValue(root, "page_fan_count"),
            PageId: pageId,
            PageName: GetString(root, "page_name"),
            PageProfilePictureUrl: pageProfilePictureUrl));
    }

    private static Result<SocialMediaUserProfile> GetThreadsProfileFromMetadata(JsonDocument metadata)
    {
        var root = metadata.RootElement;

        return Result.Success(new SocialMediaUserProfile(
            UserId: GetString(root, "user_id") ?? GetString(root, "id"),
            Username: GetString(root, "username"),
            DisplayName: GetString(root, "name") ?? GetString(root, "username"),
            ProfilePictureUrl: GetString(root, "threads_profile_picture_url") ?? GetString(root, "profile_picture_url"),
            Bio: GetString(root, "threads_biography") ?? GetString(root, "biography"),
            FollowerCount: TryGetIntValue(root, "followers_count"),
            FollowingCount: TryGetIntValue(root, "follows_count"),
            PostCount: TryGetIntValue(root, "media_count"),
            PageLikeCount: null));
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

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int? TryGetIntValue(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.String when int.TryParse(element.GetString(), out var parsedValue) => parsedValue,
            _ => null
        };
    }

    private static bool TryGetAccessToken(JsonDocument metadata, string propertyName, out string accessToken)
    {
        accessToken = string.Empty;

        if (metadata.RootElement.TryGetProperty(propertyName, out var tokenElement) &&
            tokenElement.ValueKind == JsonValueKind.String)
        {
            accessToken = tokenElement.GetString() ?? string.Empty;
            return !string.IsNullOrEmpty(accessToken);
        }

        return false;
    }
}
