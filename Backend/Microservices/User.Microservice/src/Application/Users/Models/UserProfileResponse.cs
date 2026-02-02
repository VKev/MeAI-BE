namespace Application.Users.Models;

public sealed record UserProfileResponse(
    Guid Id,
    string Username,
    string Email,
    bool EmailVerified,
    string? FullName,
    string? PhoneNumber,
    string? Provider,
    Guid? AvatarResourceId,
    string? AvatarPresignedUrl,
    string? Address,
    DateTime? Birthday,
    decimal? MeAiCoin,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<string> Roles);

