using Application.Abstractions.Data;
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

    public DeletePostCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

        await FeedModerationSupport.SoftDeletePostAsync(_unitOfWork, post, cancellationToken);
        return Result.Success(true);
    }
}
