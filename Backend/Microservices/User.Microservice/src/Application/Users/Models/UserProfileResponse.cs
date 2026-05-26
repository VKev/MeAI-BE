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
    bool TutorialStep1Completed,
    bool TutorialStep2Completed,
    DateTime? TutorialStep1CompletedAt,
    DateTime? TutorialStep2CompletedAt,
    IReadOnlyList<string> Roles);

