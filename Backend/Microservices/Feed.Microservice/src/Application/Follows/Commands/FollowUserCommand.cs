using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Common;
using Application.Follows.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Follows.Commands;

public sealed record FollowUserCommand(
    Guid FollowerId,
    Guid FolloweeId) : ICommand<FollowUserResponse>;

public sealed class FollowUserCommandHandler : ICommandHandler<FollowUserCommand, FollowUserResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeedNotificationService _feedNotificationService;

    public FollowUserCommandHandler(
        IUnitOfWork unitOfWork,
        IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<FollowUserResponse>> Handle(FollowUserCommand request, CancellationToken cancellationToken)
    {
        if (request.FollowerId == request.FolloweeId)
        {
            return Result.Failure<FollowUserResponse>(FeedErrors.CannotFollowYourself);
        }

        var alreadyFollowing = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .AnyAsync(item => item.FollowerId == request.FollowerId && item.FolloweeId == request.FolloweeId, cancellationToken);

        if (alreadyFollowing)
        {
            return Result.Failure<FollowUserResponse>(FeedErrors.AlreadyFollowing);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var follow = new Follow
        {
            Id = Guid.CreateVersion7(),
            FollowerId = request.FollowerId,
            FolloweeId = request.FolloweeId,
            CreatedAt = now
        };

        await _unitOfWork.Repository<Follow>().AddAsync(follow, cancellationToken);
        await _feedNotificationService.NotifyFollowedAsync(request.FollowerId, request.FolloweeId, cancellationToken);

        return Result.Success(new FollowUserResponse(follow.FolloweeId, follow.CreatedAt));
    }
}
