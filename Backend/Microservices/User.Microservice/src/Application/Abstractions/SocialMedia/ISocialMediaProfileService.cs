using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using System.Text.Json;

namespace Application.Abstractions.SocialMedia;

public interface ISocialMediaProfileService
{
    Task<Result<SocialMediaUserProfile>> GetUserProfileAsync(
        string type,
        JsonDocument? metadata,
        CancellationToken cancellationToken);
}

public sealed record SocialMediaUserProfile(
    string? UserId,
    string? Username,
    string? DisplayName,
    string? ProfilePictureUrl,
    string? Bio,
    int? FollowerCount,
    int? FollowingCount);
