using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record DeletePostCommand(Guid UserId, Guid PostId, bool IsAdmin = false) : ICommand<bool>;

public sealed class DeletePostCommandHandler : ICommandHandler<DeletePostCommand, bool>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public DeletePostCommandHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
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

        return Result.Success(true);
    }
}
