using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Follows;
using Application.Follows.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Follows.Queries;

public sealed record GetFollowingQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IQuery<IReadOnlyList<FollowUserResponse>>;

public sealed class GetFollowingQueryHandler : IQueryHandler<GetFollowingQuery, IReadOnlyList<FollowUserResponse>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetFollowingQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<FollowUserResponse>>> Handle(GetFollowingQuery request, CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var query = _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking()
            .Where(item => item.FollowerId == request.UserId);

        if (pagination.HasCursor)
        {
            var createdAt = pagination.CursorCreatedAt!.Value;
            var followId = pagination.CursorId!.Value;
            query = query.Where(item =>
                (item.CreatedAt < createdAt) ||
                (item.CreatedAt == createdAt && item.Id.CompareTo(followId) < 0));
        }

        var following = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(pagination.Limit)
            .Select(item => new FollowCandidate(item.Id, item.FolloweeId, item.CreatedAt))
            .ToListAsync(cancellationToken);

        return await FollowSupport.BuildFollowResponsesAsync(
            _unitOfWork,
            _userResourceService,
            following,
            cancellationToken);
    }
}
