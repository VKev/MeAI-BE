using System;
using System.Linq;
using Application.Users.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands.SetUserRole;

internal sealed class SetUserRoleCommandHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository)
    : ICommandHandler<SetUserRoleCommand, AdminUserResponse>
{
    public async Task<Result<AdminUserResponse>> Handle(SetUserRoleCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure<AdminUserResponse>(new Error("User.NotFound", "User not found"));
        }

        var roleName = ResolveRoleName(request.Role);
        if (roleName == null)
        {
            return Result.Failure<AdminUserResponse>(
                new Error("User.RoleInvalid", "Role must be ADMIN, USER, or BANNED"));
        }

        var role = await GetOrCreateRoleAsync(roleRepository, roleName, cancellationToken);

        var existingRoles = await userRoleRepository.FindAsync(ur => ur.UserId == user.Id, cancellationToken);
        if (existingRoles.Count > 0)
        {
            userRoleRepository.DeleteRange(existingRoles);
        }

        var userRole = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            RoleId = role.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await userRoleRepository.AddAsync(userRole, cancellationToken);

        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        userRepository.Update(user);

        return Result.Success(AdminUserMapping.ToResponse(user, new List<string> { role.Name }));
    }

    private static string? ResolveRoleName(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
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
