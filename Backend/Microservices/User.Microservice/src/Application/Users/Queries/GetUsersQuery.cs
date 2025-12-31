using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries;

public sealed record GetUsersQuery(bool IncludeDeleted) : IRequest<Result<List<AdminUserResponse>>>;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, Result<List<AdminUserResponse>>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;

    public GetUsersQueryHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
    }

    public async Task<Result<List<AdminUserResponse>>> Handle(GetUsersQuery request,
        CancellationToken cancellationToken)
    {
        List<User> users;
        if (request.IncludeDeleted)
        {
            users = await _userRepository.GetAll()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        else
        {
            users = await _userRepository.GetAll()
                .AsNoTracking()
                .Where(user => !user.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        if (users.Count == 0)
        {
            return Result.Success(new List<AdminUserResponse>());
        }

        var userIds = users.Select(user => user.Id).ToList();
        var userRoles = await _userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId) && !ur.IsDeleted)
            .ToListAsync(cancellationToken);

        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0
            ? new List<Role>()
            : await _roleRepository.GetAll()
                .AsNoTracking()
                .Where(role => roleIds.Contains(role.Id))
                .ToListAsync(cancellationToken);

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

        return Result.Success(responses);
    }

    private static List<string> ResolveUserRoles(
        Guid userId,
        Dictionary<Guid, List<UserRole>> rolesByUser,
        Dictionary<Guid, string> roleLookup)
    {
        if (!rolesByUser.TryGetValue(userId, out var userRoles) || userRoles.Count == 0)
        {
            return new List<string> { UserRoleConstants.User };
        }

        var roleNames = userRoles
            .Select(ur => roleLookup.TryGetValue(ur.RoleId, out var roleName) ? roleName : string.Empty)
            .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roleNames.Count == 0 ? new List<string> { UserRoleConstants.User } : roleNames;
    }
}
