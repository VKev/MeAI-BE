using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.SocialMedia;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.SocialMedia;

public sealed class SocialMediaProfileService : ISocialMediaProfileService
{
    private readonly ITikTokOAuthService _tikTokOAuthService;
    private readonly IThreadsOAuthService _threadsOAuthService;
    private readonly IFacebookOAuthService _facebookOAuthService;
    private readonly IInstagramOAuthService _instagramOAuthService;

    public SocialMediaProfileService(
        ITikTokOAuthService tikTokOAuthService,
        IThreadsOAuthService threadsOAuthService,
        IFacebookOAuthService facebookOAuthService,
        IInstagramOAuthService instagramOAuthService)
    {
        _tikTokOAuthService = tikTokOAuthService;
        _threadsOAuthService = threadsOAuthService;
        _facebookOAuthService = facebookOAuthService;
        _instagramOAuthService = instagramOAuthService;
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
            "threads" => await GetThreadsProfileAsync(accessToken, cancellationToken),
            "facebook" => await GetFacebookProfileAsync(accessToken, cancellationToken),
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
            FollowingCount: profile.FollowingCount));
    }

    private async Task<Result<SocialMediaUserProfile>> GetThreadsProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = await _threadsOAuthService.GetUserProfileAsync(accessToken, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<SocialMediaUserProfile>(result.Error);
        }

        var profile = result.Value;
        return Result.Success(new SocialMediaUserProfile(
            UserId: profile.Id,
            Username: profile.Username,
            DisplayName: profile.Name,
            ProfilePictureUrl: profile.ThreadsProfilePictureUrl,
            Bio: profile.ThreadsBiography,
            FollowerCount: null,
            FollowingCount: null));
    }

    private async Task<Result<SocialMediaUserProfile>> GetFacebookProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = await _facebookOAuthService.FetchProfileAsync(accessToken, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<SocialMediaUserProfile>(result.Error);
        }

        var profile = result.Value;
        return Result.Success(new SocialMediaUserProfile(
            UserId: profile.Id,
            Username: null,
            DisplayName: profile.Name,
            ProfilePictureUrl: null,
            Bio: null,
            FollowerCount: null,
            FollowingCount: null));
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
            DisplayName: profile.PageName,
            ProfilePictureUrl: null,
            Bio: null,
            FollowerCount: null,
            FollowingCount: null));
    }

    private static Result<SocialMediaUserProfile> GetInstagramProfileFromMetadata(JsonDocument metadata)
    {
        var root = metadata.RootElement;

        var userId = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var username = root.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() : null;
        var pageName = root.TryGetProperty("page_name", out var pageNameElement) ? pageNameElement.GetString() : null;

        return Result.Success(new SocialMediaUserProfile(
            UserId: userId,
            Username: username,
            DisplayName: pageName,
            ProfilePictureUrl: null,
            Bio: null,
            FollowerCount: null,
            FollowingCount: null));
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
