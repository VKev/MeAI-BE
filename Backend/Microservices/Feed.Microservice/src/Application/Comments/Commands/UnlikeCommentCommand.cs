using Application.Abstractions.Data;
using Application.Common;
using Application.Comments.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Comments.Commands;

public sealed record UnlikeCommentCommand(Guid UserId, Guid CommentId) : ICommand<CommentLikeResponse>;

public sealed class UnlikeCommentCommandHandler : ICommandHandler<UnlikeCommentCommand, CommentLikeResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public UnlikeCommentCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CommentLikeResponse>> Handle(UnlikeCommentCommand request, CancellationToken cancellationToken)
    {
        var comment = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .FirstOrDefaultAsync(
                item => item.Id == request.CommentId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (comment is null)
        {
            return Result.Failure<CommentLikeResponse>(FeedErrors.CommentNotFound);
        }

        var commentLike = await _unitOfWork.Repository<CommentLike>()
            .GetAll()
            .FirstOrDefaultAsync(
                item => item.CommentId == request.CommentId && item.UserId == request.UserId,
                cancellationToken);

        if (commentLike is null)
        {
            return Result.Failure<CommentLikeResponse>(FeedErrors.CommentNotLiked);
        }

        _unitOfWork.Repository<CommentLike>().Delete(commentLike);

        comment.LikesCount = Math.Max(0, comment.LikesCount - 1);
        comment.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _unitOfWork.Repository<Comment>().Update(comment);

        return Result.Success(new CommentLikeResponse(comment.Id, comment.LikesCount, false));
    }
}
