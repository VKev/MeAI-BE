using Application.Abstractions.Data;
using Application.Follows.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Follows.Queries;

public sealed record GetFollowersQuery(Guid UserId) : IQuery<IReadOnlyList<FollowUserResponse>>;

public sealed class GetFollowersQueryHandler : IQueryHandler<GetFollowersQuery, IReadOnlyList<FollowUserResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetFollowersQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<FollowUserResponse>>> Handle(GetFollowersQuery request, CancellationToken cancellationToken)
    {
        var followers = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .Where(item => item.FolloweeId == request.UserId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new FollowUserResponse(item.FollowerId, item.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<FollowUserResponse>>(followers);
    }
}
