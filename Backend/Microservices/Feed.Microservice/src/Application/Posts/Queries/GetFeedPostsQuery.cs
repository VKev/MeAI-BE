using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Posts.Queries;

public sealed record GetFeedPostsQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IQuery<IReadOnlyList<PostResponse>>;

public sealed class GetFeedPostsQueryHandler : IQueryHandler<GetFeedPostsQuery, IReadOnlyList<PostResponse>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetFeedPostsQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<SharedLibrary.Common.ResponseModel.Result<IReadOnlyList<PostResponse>>> Handle(
        GetFeedPostsQuery request,
        CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var followedUserIds = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking()
            .Where(follow => follow.FollowerId == request.UserId)
            .Select(follow => follow.FolloweeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!followedUserIds.Contains(request.UserId))
        {
            followedUserIds.Add(request.UserId);
        }

        var query = _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .Where(post => !post.IsDeleted && post.DeletedAt == null && followedUserIds.Contains(post.UserId));

        if (pagination.HasCursor)
        {
            var createdAt = pagination.CursorCreatedAt!.Value;
            var lastId = pagination.CursorId!.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        var posts = await query
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Take(pagination.Limit)
            .ToListAsync(cancellationToken);

        var response = await FeedPostSupport.ToPostResponsesAsync(
            _unitOfWork,
            _userResourceService,
            request.UserId,
            posts,
            cancellationToken);

        return SharedLibrary.Common.ResponseModel.Result.Success<IReadOnlyList<PostResponse>>(response);
    }
}
