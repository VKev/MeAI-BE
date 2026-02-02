using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Users.Commands;

public sealed record SetAvatarFromResourceCommand(
    Guid UserId,
    Guid ResourceId) : IRequest<Result<UserProfileResponse>>;

public sealed class SetAvatarFromResourceCommandHandler
    : IRequestHandler<SetAvatarFromResourceCommand, Result<UserProfileResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public SetAvatarFromResourceCommandHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<UserProfileResponse>> Handle(SetAvatarFromResourceCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<UserProfileResponse>(new Error("User.NotFound", "User not found"));
        }

        // Verify the resource exists and belongs to the user
        var resource = await _resourceRepository.GetAll()
            .FirstOrDefaultAsync(r => r.Id == request.ResourceId && !r.IsDeleted, cancellationToken);

        if (resource == null)
        {
            return Result.Failure<UserProfileResponse>(new Error("Resource.NotFound", "Resource not found"));
        }

        if (resource.UserId != request.UserId)
        {
            return Result.Failure<UserProfileResponse>(new Error("Resource.NotOwned", "Resource does not belong to user"));
        }

        // Verify the resource is an image type
        if (string.IsNullOrEmpty(resource.ContentType) || !resource.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<UserProfileResponse>(new Error("Resource.InvalidType", "Resource must be an image type"));
        }

        // Update resource type to avatar
        resource.ResourceType = "avatar";
        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _resourceRepository.Update(resource);

        // Update user avatar
        user.AvatarResourceId = request.ResourceId;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        var roles = await ResolveRolesAsync(user.Id, cancellationToken);
        var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        var avatarPresignedUrl = presignedResult.IsSuccess ? presignedResult.Value : null;
        return Result.Success(UserProfileMapping.ToResponse(user, roles, avatarPresignedUrl));
    }

    private async Task<List<string>> ResolveRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRoles = await _userRoleRepository.GetAll()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .ToListAsync(cancellationToken);

        if (userRoles.Count == 0)
        {
            return [UserRoleConstants.User];
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

        return roleNames.Count == 0 ? [UserRoleConstants.User] : roleNames;
    }
}

