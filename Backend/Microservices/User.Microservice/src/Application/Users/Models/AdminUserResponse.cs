namespace Application.Users.Models;

public sealed record AdminUserResponse(
    Guid Id,
    string Username,
    string Email,
    bool EmailVerified,
    string? FullName,
    string? PhoneNumber,
    string? Provider,
    Guid? AvatarResourceId,
    string? Address,
    DateTime? Birthday,
    decimal? MeAiCoin,
    bool IsDeleted,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt,
    IReadOnlyList<string> Roles);
