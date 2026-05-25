using Application.Abstractions.Ai;
using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record DeletePostCommand(
    Guid UserId,
    Guid PostId,
    bool IsAdmin = false,
    bool SkipAiMirrorDelete = false) : ICommand<bool>;

public sealed class DeletePostCommandHandler : ICommandHandler<DeletePostCommand, bool>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;
    private readonly IAiFeedPostService _aiFeedPostService;
    private readonly IFeedNotificationService _feedNotificationService;

    public DeletePostCommandHandler(
        IUnitOfWork unitOfWork,
        IUserResourceService userResourceService,
        IAiFeedPostService aiFeedPostService,
        IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
        _aiFeedPostService = aiFeedPostService;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<bool>> Handle(DeletePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<bool>(FeedErrors.PostNotFound);
        }

        if (post.UserId != request.UserId && !request.IsAdmin)
        {
            return Result.Failure<bool>(FeedErrors.Forbidden);
        }

        var resourceIds = post.ResourceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        await FeedModerationSupport.SoftDeletePostAsync(_unitOfWork, post, cancellationToken);

        if (resourceIds.Count > 0)
        {
            var deleteResourcesResult = await _userResourceService.DeleteResourcesAsync(
                post.UserId,
                resourceIds,
                cancellationToken);

            if (deleteResourcesResult.IsFailure)
            {
                return Result.Failure<bool>(deleteResourcesResult.Error);
            }
        }

        if (post.AiPostId.HasValue && !request.SkipAiMirrorDelete)
        {
            var deleteMirrorResult = await _aiFeedPostService.DeleteMirrorPostAsync(
                new DeleteAiMirrorPostRequest(post.UserId, post.AiPostId.Value),
                cancellationToken);

            if (deleteMirrorResult.IsFailure)
            {
                return Result.Failure<bool>(deleteMirrorResult.Error);
            }
        }

        if (request.IsAdmin && request.UserId != post.UserId)
        {
            await _feedNotificationService.NotifyModerationActionAsync(
                request.UserId,
                post.UserId,
                null,
                "Post",
                post.Id,
                post.Id,
                null,
                FeedModerationSupport.ResolvedStatus,
                FeedModerationSupport.DeleteTargetPostAction,
                null,
                FeedPostSupport.BuildPreview(post.Content),
                cancellationToken);
        }

        return Result.Success(true);
    }
}
