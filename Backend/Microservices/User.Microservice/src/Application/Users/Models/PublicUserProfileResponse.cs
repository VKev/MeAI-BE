namespace Application.Users.Models;

public sealed record PublicUserProfileResponse(
    Guid Id,
    string Username,
    string? FullName,
    string? AvatarPresignedUrl);
