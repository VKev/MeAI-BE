using Domain.Entities;
using Application.Users.Models;

namespace Application.Users;

internal static class UserProfileMapping
{
    internal static UserProfileResponse ToResponse(User user, IReadOnlyList<string> roles) =>
        new(
            user.Id,
            user.Username ?? string.Empty,
            user.Email ?? string.Empty,
            user.EmailVerified,
            user.FullName,
            user.PhoneNumber,
            user.Provider,
            user.AvatarResourceId,
            user.Address,
            user.Birthday,
            user.MeAiCoin,
            user.CreatedAt,
            user.UpdatedAt,
            roles);
}
