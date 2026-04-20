using Application.Abstractions.Data;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Comments.Commands;

public sealed record DeleteCommentCommand(Guid UserId, Guid CommentId) : ICommand<bool>;

public sealed class DeleteCommentCommandHandler : ICommandHandler<DeleteCommentCommand, bool>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCommentCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var comment = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.CommentId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (comment is null)
        {
            return Result.Failure<bool>(FeedErrors.CommentNotFound);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == comment.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<bool>(FeedErrors.PostNotFound);
        }

        if (post.UserId != request.UserId && comment.UserId != request.UserId)
        {
            return Result.Failure<bool>(FeedErrors.Forbidden);
        }

        var deletedCount = await FeedModerationSupport.SoftDeleteCommentThreadAsync(_unitOfWork, post, comment, cancellationToken);
        if (deletedCount == 0)
        {
            return Result.Failure<bool>(FeedErrors.CommentNotFound);
        }

        return Result.Success(true);
    }
}
