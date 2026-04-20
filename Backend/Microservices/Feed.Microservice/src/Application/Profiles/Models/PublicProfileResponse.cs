namespace Application.Profiles.Models;

public sealed record PublicProfileResponse(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl,
    int FollowersCount,
    int FollowingCount,
    bool? IsFollowedByCurrentUser);
