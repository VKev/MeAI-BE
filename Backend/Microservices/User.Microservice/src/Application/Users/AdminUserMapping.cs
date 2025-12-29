using Application.Users.Contracts;
using Domain.Entities;

namespace Application.Users;

internal static class AdminUserMapping
{
    internal static AdminUserResponse ToResponse(User user, IReadOnlyList<string> roles) =>
        new(
            user.Id,
            user.Username,
            user.Email,
            user.EmailVerified,
            user.FullName,
            user.PhoneNumber,
            user.Provider,
            user.AvatarResourceId,
            user.Address,
            user.Birthday,
            user.MeAiCoin,
            user.IsDeleted,
            user.CreatedAt,
            user.UpdatedAt,
            user.DeletedAt,
            roles);
}
