using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Profiles.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Profiles.Queries;

public sealed record GetPublicProfileByUsernameQuery(string Username) : IQuery<PublicProfileResponse>;

public sealed class GetPublicProfileByUsernameQueryHandler : IQueryHandler<GetPublicProfileByUsernameQuery, PublicProfileResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetPublicProfileByUsernameQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<PublicProfileResponse>> Handle(GetPublicProfileByUsernameQuery request, CancellationToken cancellationToken)
    {
        var username = FeedModerationSupport.NormalizeUsername(request.Username);
        if (username is null)
        {
            return Result.Failure<PublicProfileResponse>(FeedErrors.InvalidUsername);
        }

        var profileResult = await _userResourceService.GetPublicUserProfileByUsernameAsync(username, cancellationToken);
        if (profileResult.IsFailure)
        {
            return Result.Failure<PublicProfileResponse>(MapUserProfileError(profileResult.Error));
        }

        var userId = profileResult.Value.UserId;
        var followersCount = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking()
            .CountAsync(item => item.FolloweeId == userId, cancellationToken);

        var followingCount = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking()
            .CountAsync(item => item.FollowerId == userId, cancellationToken);

        return Result.Success(new PublicProfileResponse(
            userId,
            profileResult.Value.Username,
            profileResult.Value.FullName,
            profileResult.Value.AvatarUrl,
            followersCount,
            followingCount));
    }

    private static Error MapUserProfileError(Error error)
    {
        return string.Equals(error.Code, "UserResources.NotFound", StringComparison.Ordinal)
            ? FeedErrors.UserNotFound
            : error;
    }
}
