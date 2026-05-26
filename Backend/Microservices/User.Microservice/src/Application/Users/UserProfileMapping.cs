using Domain.Entities;
using Application.Users.Models;

namespace Application.Users;

internal static class UserProfileMapping
{
    internal static UserProfileResponse ToResponse(User user, IReadOnlyList<string> roles, string? avatarPresignedUrl = null) =>
        new(
            user.Id,
            user.Username ?? string.Empty,
            user.Email ?? string.Empty,
            user.EmailVerified,
            user.FullName,
            user.PhoneNumber,
            user.Provider,
            user.AvatarResourceId,
            avatarPresignedUrl,
            user.Address,
            user.Birthday,
            user.MeAiCoin,
            user.CreatedAt,
            user.UpdatedAt,
            user.TutorialStep1CompletedAt.HasValue,
            user.TutorialStep2CompletedAt.HasValue,
            user.TutorialStep1CompletedAt,
            user.TutorialStep2CompletedAt,
            roles);
}
