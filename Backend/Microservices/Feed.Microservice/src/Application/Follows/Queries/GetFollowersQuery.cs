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

public sealed record GetFollowersQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IQuery<IReadOnlyList<FollowUserResponse>>;

public sealed class GetFollowersQueryHandler : IQueryHandler<GetFollowersQuery, IReadOnlyList<FollowUserResponse>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetFollowersQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<FollowUserResponse>>> Handle(GetFollowersQuery request, CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var query = _unitOfWork.Repository<Follow>()
            .GetAll()
            .AsNoTracking()
            .Where(item => item.FolloweeId == request.UserId);

        if (pagination.HasCursor)
        {
            var createdAt = pagination.CursorCreatedAt!.Value;
            var followId = pagination.CursorId!.Value;
            query = query.Where(item =>
                (item.CreatedAt < createdAt) ||
                (item.CreatedAt == createdAt && item.Id.CompareTo(followId) < 0));
        }

        var followers = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(pagination.Limit)
            .Select(item => new FollowCandidate(item.Id, item.FollowerId, item.CreatedAt))
            .ToListAsync(cancellationToken);

        return await FollowSupport.BuildFollowResponsesAsync(
            _unitOfWork,
            _userResourceService,
            followers,
            cancellationToken);
    }
}
