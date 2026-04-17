using Application.Abstractions.Data;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Follows.Commands;

public sealed record UnfollowUserCommand(
    Guid FollowerId,
    Guid FolloweeId) : ICommand<bool>;

public sealed class UnfollowUserCommandHandler : ICommandHandler<UnfollowUserCommand, bool>
{
    private readonly IUnitOfWork _unitOfWork;

    public UnfollowUserCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(UnfollowUserCommand request, CancellationToken cancellationToken)
    {
        var follow = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.FollowerId == request.FollowerId && item.FolloweeId == request.FolloweeId, cancellationToken);

        if (follow is null)
        {
            return Result.Failure<bool>(FeedErrors.NotFollowing);
        }

        _unitOfWork.Repository<Follow>().Delete(follow);
        return Result.Success(true);
    }
}
