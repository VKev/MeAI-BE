using System;
using System.Linq;
using Application.Users.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries.GetUsers;

internal sealed class GetUsersQueryHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<AdminUserResponse>>
{
    public async Task<Result<IReadOnlyList<AdminUserResponse>>> Handle(GetUsersQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<User> users = request.IncludeDeleted
            ? await userRepository.GetAllAsync(cancellationToken)
            : await userRepository.FindAsync(user => !user.IsDeleted, cancellationToken);

        if (users.Count == 0)
        {
            return Result.Success<IReadOnlyList<AdminUserResponse>>(Array.Empty<AdminUserResponse>());
        }

        var userIds = users.Select(user => user.Id).ToList();
        var userRoles = await userRoleRepository.FindAsync(
            ur => userIds.Contains(ur.UserId) && !ur.IsDeleted,
            cancellationToken);

        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0
            ? new List<Role>()
            : (await roleRepository.FindAsync(role => roleIds.Contains(role.Id), cancellationToken)).ToList();

        var roleLookup = roles
            .Where(role => !string.IsNullOrWhiteSpace(role.Name))
            .ToDictionary(role => role.Id, role => role.Name);

        var rolesByUser = userRoles
            .GroupBy(ur => ur.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var responses = users.Select(user =>
        {
            var roleNames = ResolveUserRoles(user.Id, rolesByUser, roleLookup);
            return AdminUserMapping.ToResponse(user, roleNames);
        }).ToList();

        return Result.Success<IReadOnlyList<AdminUserResponse>>(responses);
    }

    private static List<string> ResolveUserRoles(
        Guid userId,
        IReadOnlyDictionary<Guid, List<UserRole>> rolesByUser,
        IReadOnlyDictionary<Guid, string> roleLookup)
    {
        if (!rolesByUser.TryGetValue(userId, out var userRoles) || userRoles.Count == 0)
        {
            return new List<string> { UserRoleConstants.User };
        }

        var roleNames = userRoles
            .Select(ur => roleLookup.TryGetValue(ur.RoleId, out var roleName) ? roleName : null)
            .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roleNames.Count == 0 ? new List<string> { UserRoleConstants.User } : roleNames;
    }
}
