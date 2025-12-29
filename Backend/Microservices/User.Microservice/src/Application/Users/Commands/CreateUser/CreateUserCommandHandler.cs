using System;
using System.Linq;
using Application.Users.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.CreateUser;

internal sealed class CreateUserCommandHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository,
    IPasswordHasher passwordHasher)
    : ICommandHandler<CreateUserCommand, AdminUserResponse>
{
    public async Task<Result<AdminUserResponse>> Handle(CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var normalizedUsername = NormalizeUsername(request.Username);

        var existingUsers = await userRepository.FindAsync(
            user => user.Email.ToLower() == normalizedEmail || user.Username.ToLower() == normalizedUsername,
            cancellationToken);

        if (existingUsers.Any(user => user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<AdminUserResponse>(new Error("User.EmailTaken", "Email is already registered"));
        }

        if (existingUsers.Any(user => user.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<AdminUserResponse>(new Error("User.UsernameTaken", "Username is already taken"));
        }

        var roleName = ResolveRoleName(request.Role);
        if (roleName == null)
        {
            return Result.Failure<AdminUserResponse>(
                new Error("User.RoleInvalid", "Role must be ADMIN, USER, or BANNED"));
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = request.Username.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Birthday = request.Birthday,
            AvatarResourceId = request.AvatarResourceId,
            MeAiCoin = request.MeAiCoin ?? 0m,
            Provider = "admin",
            EmailVerified = request.EmailVerified ?? false,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await userRepository.AddAsync(user, cancellationToken);

        var role = await GetOrCreateRoleAsync(roleRepository, roleName, cancellationToken);
        var userRole = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            RoleId = role.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await userRoleRepository.AddAsync(userRole, cancellationToken);

        return Result.Success(AdminUserMapping.ToResponse(user, new List<string> { role.Name }));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    private static string? ResolveRoleName(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return UserRoleConstants.User;
        }

        var normalized = role.Trim().ToUpperInvariant();
        return UserRoleConstants.AllowedRoles.Contains(normalized) ? normalized : null;
    }

    private static async Task<Role> GetOrCreateRoleAsync(
        IRepository<Role> roleRepository,
        string roleName,
        CancellationToken cancellationToken)
    {
        var roles = await roleRepository.FindAsync(role => role.Name == roleName, cancellationToken);
        var role = roles.FirstOrDefault();
        if (role != null) return role;

        role = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = roleName,
            Description = ResolveRoleDescription(roleName),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await roleRepository.AddAsync(role, cancellationToken);
        return role;
    }

    private static string ResolveRoleDescription(string roleName) =>
        roleName switch
        {
            UserRoleConstants.Admin => "Administrator",
            UserRoleConstants.Banned => "Banned user",
            _ => "Standard user"
        };
}
