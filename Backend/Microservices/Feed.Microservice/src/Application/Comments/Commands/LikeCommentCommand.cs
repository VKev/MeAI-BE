using Application.Abstractions.Data;
using Application.Common;
using Application.Comments.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Comments.Commands;

public sealed record LikeCommentCommand(Guid UserId, Guid CommentId) : ICommand<CommentLikeResponse>;

public sealed class LikeCommentCommandHandler : ICommandHandler<LikeCommentCommand, CommentLikeResponse>
{
    private const string CommentLikeUniqueIndexName = "ix_comment_likes_comment_user_id";
    private const string UniqueViolationSqlState = "23505";

    private readonly IUnitOfWork _unitOfWork;

    public LikeCommentCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CommentLikeResponse>> Handle(LikeCommentCommand request, CancellationToken cancellationToken)
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

        var alreadyLiked = await _unitOfWork.Repository<CommentLike>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(item => item.CommentId == request.CommentId && item.UserId == request.UserId, cancellationToken);

        if (alreadyLiked)
        {
            return Result.Failure<CommentLikeResponse>(FeedErrors.CommentAlreadyLiked);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;

        await _unitOfWork.Repository<CommentLike>().AddAsync(new CommentLike
        {
            Id = Guid.CreateVersion7(),
            CommentId = request.CommentId,
            UserId = request.UserId,
            CreatedAt = now
        }, cancellationToken);

        comment.LikesCount += 1;
        comment.UpdatedAt = now;
        _unitOfWork.Repository<Comment>().Update(comment);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateCommentLikeViolation(exception))
        {
            comment.LikesCount = Math.Max(0, comment.LikesCount - 1);
            return Result.Failure<CommentLikeResponse>(FeedErrors.CommentAlreadyLiked);
        }

        return Result.Success(new CommentLikeResponse(comment.Id, comment.LikesCount, true));
    }

    private static bool IsDuplicateCommentLikeViolation(DbUpdateException exception)
    {
        var sqlState = exception.InnerException?.GetType().GetProperty("SqlState")?.GetValue(exception.InnerException) as string;
        var constraintName = exception.InnerException?.GetType().GetProperty("ConstraintName")?.GetValue(exception.InnerException) as string;

        if (sqlState == UniqueViolationSqlState
            && string.Equals(constraintName, CommentLikeUniqueIndexName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return exception.InnerException?.Message.Contains(CommentLikeUniqueIndexName, StringComparison.OrdinalIgnoreCase) == true
               || exception.Message.Contains(CommentLikeUniqueIndexName, StringComparison.OrdinalIgnoreCase);
    }
}
