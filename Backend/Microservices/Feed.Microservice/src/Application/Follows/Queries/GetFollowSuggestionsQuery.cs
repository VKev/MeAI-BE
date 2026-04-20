using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Follows.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Follows.Queries;

public sealed record GetFollowSuggestionsQuery(
    Guid UserId,
    int? Limit) : IQuery<IReadOnlyList<FollowSuggestionResponse>>;

public sealed class GetFollowSuggestionsQueryHandler : IQueryHandler<GetFollowSuggestionsQuery, IReadOnlyList<FollowSuggestionResponse>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetFollowSuggestionsQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<FollowSuggestionResponse>>> Handle(GetFollowSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit ?? FeedPaginationSupport.DefaultPageSize, 1, FeedPaginationSupport.MaxPageSize);

        var excludedUserIds = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking()
            .Where(item => item.FollowerId == request.UserId)
            .Select(item => item.FolloweeId)
            .ToListAsync(cancellationToken);

        if (!excludedUserIds.Contains(request.UserId))
        {
            excludedUserIds.Add(request.UserId);
        }

        var rankedUsers = (await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .Where(post =>
                !post.IsDeleted &&
                post.DeletedAt == null &&
                !excludedUserIds.Contains(post.UserId))
            .GroupBy(post => post.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                PostCount = group.Count(),
                LatestPostCreatedAt = group.Max(post => post.CreatedAt)
            })
            .OrderByDescending(item => item.PostCount)
            .ThenByDescending(item => item.LatestPostCreatedAt)
            .ThenBy(item => item.UserId)
            .Take(limit)
            .ToListAsync(cancellationToken))
            .Select(item => new RankedFollowSuggestionCandidate(
                item.UserId,
                item.PostCount,
                item.LatestPostCreatedAt))
            .ToList();

        if (rankedUsers.Count == 0)
        {
            return Result.Success<IReadOnlyList<FollowSuggestionResponse>>(Array.Empty<FollowSuggestionResponse>());
        }

        var profilesResult = await _userResourceService.GetPublicUserProfilesByIdsAsync(
            rankedUsers.Select(item => item.UserId).ToList(),
            cancellationToken);

        if (profilesResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FollowSuggestionResponse>>(profilesResult.Error);
        }

        var response = rankedUsers
            .Where(item => profilesResult.Value.ContainsKey(item.UserId))
            .Select(item =>
            {
                var profile = profilesResult.Value[item.UserId];
                return new FollowSuggestionResponse(
                    item.UserId,
                    profile.Username,
                    profile.FullName,
                    profile.AvatarUrl,
                    item.PostCount);
            })
            .ToList();

        return Result.Success<IReadOnlyList<FollowSuggestionResponse>>(response);
    }

    private sealed record RankedFollowSuggestionCandidate(
        Guid UserId,
        int PostCount,
        DateTime? LatestPostCreatedAt);
}
