using System;
using System.Linq;
using Application.Users.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.UpdateUser;

internal sealed class UpdateUserCommandHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository,
    IPasswordHasher passwordHasher)
    : ICommandHandler<UpdateUserCommand, AdminUserResponse>
{
    public async Task<Result<AdminUserResponse>> Handle(UpdateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure<AdminUserResponse>(new Error("User.NotFound", "User not found"));
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var normalizedUsername = NormalizeUsername(request.Username);
            var existingUsers = await userRepository.FindAsync(
                u => u.Username.ToLower() == normalizedUsername && u.Id != user.Id,
                cancellationToken);

            if (existingUsers.Count > 0)
            {
                return Result.Failure<AdminUserResponse>(
                    new Error("User.UsernameTaken", "Username is already taken"));
            }

            user.Username = request.Username.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = NormalizeEmail(request.Email);
            var existingUsers = await userRepository.FindAsync(
                u => u.Email.ToLower() == normalizedEmail && u.Id != user.Id,
                cancellationToken);

            if (existingUsers.Count > 0)
            {
                return Result.Failure<AdminUserResponse>(
                    new Error("User.EmailTaken", "Email is already registered"));
            }

            user.Email = normalizedEmail;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = passwordHasher.HashPassword(request.Password);
        }

        if (request.FullName != null)
        {
            user.FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
        }

        if (request.PhoneNumber != null)
        {
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        }

        if (request.Address != null)
        {
            user.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        }

        if (request.Birthday.HasValue)
        {
            user.Birthday = request.Birthday;
        }

        if (request.AvatarResourceId.HasValue)
        {
            user.AvatarResourceId = request.AvatarResourceId;
        }

        if (request.MeAiCoin.HasValue)
        {
            user.MeAiCoin = request.MeAiCoin;
        }

        if (request.EmailVerified.HasValue)
        {
            user.EmailVerified = request.EmailVerified.Value;
        }

        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        userRepository.Update(user);

        var roles = await ResolveRolesAsync(user.Id, roleRepository, userRoleRepository, cancellationToken);
        return Result.Success(AdminUserMapping.ToResponse(user, roles));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    private static async Task<List<string>> ResolveRolesAsync(
        Guid userId,
        IRepository<Role> roleRepository,
        IRepository<UserRole> userRoleRepository,
        CancellationToken cancellationToken)
    {
        var userRoles = await userRoleRepository.FindAsync(
            ur => ur.UserId == userId && !ur.IsDeleted,
            cancellationToken);
        if (userRoles.Count == 0)
        {
            return new List<string> { UserRoleConstants.User };
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await roleRepository.FindAsync(role => roleIds.Contains(role.Id), cancellationToken);
        var roleNames = roles.Select(role => role.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return roleNames.Count == 0 ? new List<string> { UserRoleConstants.User } : roleNames;
    }
}
