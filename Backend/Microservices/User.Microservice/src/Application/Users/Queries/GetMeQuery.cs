using Application.Abstractions.Data;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries;

public sealed record GetMeQuery(Guid UserId) : IRequest<Result<UserProfileResponse>>;

public sealed class GetMeQueryHandler
    : IRequestHandler<GetMeQuery, Result<UserProfileResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;

    public GetMeQueryHandler(IUnitOfWork unitOfWork)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
    }

    public async Task<Result<UserProfileResponse>> Handle(GetMeQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<UserProfileResponse>(new Error("User.NotFound", "User not found"));
        }

        var roles = await ResolveRolesAsync(user.Id, cancellationToken);
        return Result.Success(UserProfileMapping.ToResponse(user, roles));
    }

    private async Task<List<string>> ResolveRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRoles = await _userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .ToListAsync(cancellationToken);

        if (userRoles.Count == 0)
        {
            return new List<string> { UserRoleConstants.User };
        }

        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await _roleRepository.GetAll()
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToListAsync(cancellationToken);

        var roleNames = roles
            .Select(role => role.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return roleNames.Count == 0 ? new List<string> { UserRoleConstants.User } : roleNames;
    }
}
