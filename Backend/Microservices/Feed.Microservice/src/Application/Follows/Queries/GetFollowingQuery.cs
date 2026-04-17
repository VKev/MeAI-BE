using Application.Abstractions.Data;
using Application.Follows.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Follows.Queries;

public sealed record GetFollowingQuery(Guid UserId) : IQuery<IReadOnlyList<FollowUserResponse>>;

public sealed class GetFollowingQueryHandler : IQueryHandler<GetFollowingQuery, IReadOnlyList<FollowUserResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetFollowingQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<FollowUserResponse>>> Handle(GetFollowingQuery request, CancellationToken cancellationToken)
    {
        var following = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .Where(item => item.FollowerId == request.UserId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new FollowUserResponse(item.FolloweeId, item.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<FollowUserResponse>>(following);
    }
}
