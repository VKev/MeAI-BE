using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Users.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Queries;

public sealed record GetPublicUserProfileByUsernameQuery(string Username) : IRequest<Result<PublicUserProfileResponse>>;

public sealed class GetPublicUserProfileByUsernameQueryHandler
    : IRequestHandler<GetPublicUserProfileByUsernameQuery, Result<PublicUserProfileResponse>>
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public GetPublicUserProfileByUsernameQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _userRepository = unitOfWork.Repository<User>();
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<PublicUserProfileResponse>> Handle(
        GetPublicUserProfileByUsernameQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        var user = await _userRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => !item.IsDeleted && item.Username.ToLower() == normalizedUsername,
                cancellationToken);

        if (user is null)
        {
            return Result.Failure<PublicUserProfileResponse>(new Error("User.NotFound", "User not found"));
        }

        var avatarPresignedUrl = await GetAvatarPresignedUrlAsync(user.AvatarResourceId, cancellationToken);

        return Result.Success(new PublicUserProfileResponse(
            user.Id,
            user.Username,
            user.FullName,
            avatarPresignedUrl));
    }

    private async Task<string?> GetAvatarPresignedUrlAsync(Guid? avatarResourceId, CancellationToken cancellationToken)
    {
        if (!avatarResourceId.HasValue)
        {
            return null;
        }

        var resource = await _resourceRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == avatarResourceId.Value && !item.IsDeleted,
                cancellationToken);

        if (resource is null)
        {
            return null;
        }

        var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        return presignedResult.IsSuccess ? presignedResult.Value : null;
    }
}
