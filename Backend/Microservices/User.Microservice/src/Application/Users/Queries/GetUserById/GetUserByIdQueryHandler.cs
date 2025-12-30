using Application.Users.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries.GetUserById;

internal sealed class GetUserByIdQueryHandler(
    IRepository<User> userRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository)
    : IQueryHandler<GetUserByIdQuery, AdminUserResponse>
{
    public async Task<Result<AdminUserResponse>> Handle(GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure<AdminUserResponse>(new Error("User.NotFound", "User not found"));
        }

        var roles = await ResolveRolesAsync(user.Id, roleRepository, userRoleRepository, cancellationToken);
        return Result.Success(AdminUserMapping.ToResponse(user, roles));
    }

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
            return [UserRoleConstants.User];
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await roleRepository.FindAsync(role => roleIds.Contains(role.Id), cancellationToken);
        var roleNames = roles.Select(role => role.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return roleNames.Count == 0 ? [UserRoleConstants.User] : roleNames;
    }
}
