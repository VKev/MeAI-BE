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

public sealed record UpdateAvatarCommand(
    Guid UserId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long ContentLength) : IRequest<Result<UserProfileResponse>>;

public sealed class UpdateAvatarCommandHandler
    : IRequestHandler<UpdateAvatarCommand, Result<UserProfileResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IObjectStorageService _objectStorageService;

    public UpdateAvatarCommandHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<UserProfileResponse>> Handle(UpdateAvatarCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.UserId, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Failure<UserProfileResponse>(new Error("User.NotFound", "User not found"));
        }

        var resourceId = Guid.CreateVersion7();
        var extension = Path.GetExtension(request.FileName);
        var key = AvatarStorageKey.Build(request.UserId, resourceId, extension);

        var uploadRequest = new StorageUploadRequest(
            key,
            request.FileStream,
            request.ContentType,
            request.ContentLength);

        var uploadResult = await _objectStorageService.UploadAsync(uploadRequest, cancellationToken);
        if (uploadResult.IsFailure)
        {
            return Result.Failure<UserProfileResponse>(uploadResult.Error);
        }

        var oldAvatarResourceId = user.AvatarResourceId;
        user.AvatarResourceId = resourceId;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _userRepository.Update(user);

        // Optionally delete old avatar (best effort, don't fail if it doesn't work)
        if (oldAvatarResourceId.HasValue)
        {
            try
            {
                var oldKey = AvatarStorageKey.Build(request.UserId, oldAvatarResourceId.Value, extension);
                await _objectStorageService.DeleteAsync(oldKey, cancellationToken);
            }
            catch
            {
                // Ignore deletion errors - old avatar will be orphaned
            }
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

internal static class AvatarStorageKey
{
    internal static string Build(Guid userId, Guid resourceId, string extension) =>
        $"avatars/{userId}/{resourceId}{extension}";
}
