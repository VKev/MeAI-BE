using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Follows.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common.ResponseModel;

namespace Application.Follows;

internal static class FollowSupport
{
    public static async Task<Result<IReadOnlyList<FollowUserResponse>>> BuildFollowResponsesAsync(
        IUnitOfWork unitOfWork,
        IUserResourceService userResourceService,
        IReadOnlyList<FollowCandidate> follows,
        CancellationToken cancellationToken)
    {
        if (follows.Count == 0)
        {
            return Result.Success<IReadOnlyList<FollowUserResponse>>(Array.Empty<FollowUserResponse>());
        }

        var userIds = follows
            .Select(item => item.UserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var profilesResult = await userResourceService.GetPublicUserProfilesByIdsAsync(userIds, cancellationToken);
        if (profilesResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FollowUserResponse>>(profilesResult.Error);
        }

        var postCountsByUserId = await unitOfWork.Repository<Post>()
            .GetAll()
            .Where(post =>
                !post.IsDeleted &&
                post.DeletedAt == null &&
                userIds.Contains(post.UserId))
            .GroupBy(post => post.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                PostCount = group.Count()
            })
            .ToDictionaryAsync(item => item.UserId, item => item.PostCount, cancellationToken);

        var response = follows
            .Where(item => profilesResult.Value.ContainsKey(item.UserId))
            .Select(item =>
            {
                var profile = profilesResult.Value[item.UserId];
                return new FollowUserResponse(
                    item.FollowId,
                    item.UserId,
                    profile.Username,
                    profile.FullName,
                    profile.AvatarUrl,
                    postCountsByUserId.GetValueOrDefault(item.UserId, 0),
                    item.FollowedAt);
            })
            .ToList();

        return Result.Success<IReadOnlyList<FollowUserResponse>>(response);
    }
}

internal sealed record FollowCandidate(
    Guid FollowId,
    Guid UserId,
    DateTime? FollowedAt);
