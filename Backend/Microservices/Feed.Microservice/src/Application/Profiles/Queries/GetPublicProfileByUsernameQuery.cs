using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Profiles.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Profiles.Queries;

public sealed record GetPublicProfileByUsernameQuery(string Username, Guid? RequestingUserId) : IQuery<PublicProfileResponse>;

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
        var follows = _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking();
        var posts = _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking();

        var followersCount = await follows
            .CountAsync(item => item.FolloweeId == userId, cancellationToken);

        var followingCount = await follows
            .CountAsync(item => item.FollowerId == userId, cancellationToken);

        var postCount = await posts
            .CountAsync(item => item.UserId == userId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        bool? isFollowedByCurrentUser = null;
        if (request.RequestingUserId.HasValue)
        {
            isFollowedByCurrentUser = request.RequestingUserId.Value == userId || await follows
                .AnyAsync(
                    item => item.FollowerId == request.RequestingUserId.Value && item.FolloweeId == userId,
                    cancellationToken);
        }

        return Result.Success(new PublicProfileResponse(
            userId,
            profileResult.Value.Username,
            profileResult.Value.FullName,
            profileResult.Value.AvatarUrl,
            followersCount,
            followingCount,
            postCount,
            isFollowedByCurrentUser));
    }

    private static Error MapUserProfileError(Error error)
    {
        return string.Equals(error.Code, "UserResources.NotFound", StringComparison.Ordinal)
            ? FeedErrors.UserNotFound
            : error;
    }
}
