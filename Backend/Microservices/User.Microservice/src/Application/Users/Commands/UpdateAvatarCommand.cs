using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources;
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
    long ContentLength,
    string? Status,
    string? ResourceType) : IRequest<Result<UserProfileResponse>>;

public sealed class UpdateAvatarCommandHandler
    : IRequestHandler<UpdateAvatarCommand, Result<UserProfileResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Role> _roleRepository;
    private readonly IRepository<UserRole> _userRoleRepository;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public UpdateAvatarCommandHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _userRepository = unitOfWork.Repository<User>();
        _roleRepository = unitOfWork.Repository<Role>();
        _userRoleRepository = unitOfWork.Repository<UserRole>();
        _resourceRepository = unitOfWork.Repository<Resource>();
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
        var storageKey = ResourceStorageKey.Build(request.UserId, resourceId);

        await using var fileStream = request.FileStream;
        var uploadResult = await _objectStorageService.UploadAsync(
            new StorageUploadRequest(
                storageKey,
                fileStream,
                request.ContentType,
                request.ContentLength),
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            return Result.Failure<UserProfileResponse>(uploadResult.Error);
        }

        // Create new resource for avatar
        var resource = new Resource
        {
            Id = resourceId,
            UserId = request.UserId,
            Link = uploadResult.Value.Key,
            Status = request.Status?.Trim() ?? "active",
            ResourceType = request.ResourceType?.Trim() ?? "avatar",
            ContentType = request.ContentType.Trim(),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _resourceRepository.AddAsync(resource, cancellationToken);

        user.AvatarResourceId = resourceId;
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
