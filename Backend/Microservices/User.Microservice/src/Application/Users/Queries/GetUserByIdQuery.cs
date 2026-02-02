using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<Result<AdminUserResponse>>;

public sealed class GetUserByIdQueryHandler
    : IRequestHandler<GetUserByIdQuery, Result<AdminUserResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public GetUserByIdQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<AdminUserResponse>> Handle(GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure<AdminUserResponse>(new Error("User.NotFound", "User not found"));
        }

        var roles = await ResolveRolesAsync(user.Id, cancellationToken);
        var avatarPresignedUrl = await GetAvatarPresignedUrlAsync(user.AvatarResourceId, cancellationToken);
        return Result.Success(AdminUserMapping.ToResponse(user, roles, avatarPresignedUrl));
    }

    private async Task<string?> GetAvatarPresignedUrlAsync(Guid? avatarResourceId, CancellationToken cancellationToken)
    {
        if (!avatarResourceId.HasValue)
            return null;

        var resource = await _resourceRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == avatarResourceId.Value && !r.IsDeleted, cancellationToken);

        if (resource == null)
            return null;

        var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        return presignedResult.IsSuccess ? presignedResult.Value : null;
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

        var roleNames = roles.Select(role => role.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return roleNames.Count == 0 ? new List<string> { UserRoleConstants.User } : roleNames;
    }
}

