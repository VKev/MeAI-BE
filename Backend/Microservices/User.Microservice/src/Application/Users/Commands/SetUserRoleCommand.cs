using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record SetUserRoleCommand(Guid UserId, string Role) : IRequest<Result<AdminUserResponse>>;

public sealed class SetUserRoleCommandHandler
    : IRequestHandler<SetUserRoleCommand, Result<AdminUserResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;

    public SetUserRoleCommandHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
    }

    public async Task<Result<AdminUserResponse>> Handle(SetUserRoleCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
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

        var role = await GetOrCreateRoleAsync(roleName, cancellationToken);

        var existingRoles = await _userRoleRepository.GetAll()
            .Where(ur => ur.UserId == user.Id)
            .ToListAsync(cancellationToken);
        if (existingRoles.Count > 0)
        {
            _userRoleRepository.DeleteRange(existingRoles);
        }

        var userRole = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            RoleId = role.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _userRoleRepository.AddAsync(userRole, cancellationToken);

        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

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

    private async Task<Role> GetOrCreateRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);

        if (role != null)
        {
            return role;
        }

        role = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = roleName,
            Description = ResolveRoleDescription(roleName),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _roleRepository.AddAsync(role, cancellationToken);
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
