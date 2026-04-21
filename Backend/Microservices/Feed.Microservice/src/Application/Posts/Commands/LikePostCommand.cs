using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Common;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record LikePostCommand(Guid UserId, Guid PostId) : ICommand<PostLikeResponse>;

public sealed class LikePostCommandHandler : ICommandHandler<LikePostCommand, PostLikeResponse>
{
    private const string PostLikeUniqueIndexName = "ix_post_likes_post_id_user_id";
    private const string UniqueViolationSqlState = "23505";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeedNotificationService _feedNotificationService;

    public LikePostCommandHandler(IUnitOfWork unitOfWork, IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<PostLikeResponse>> Handle(LikePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(
                item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (post is null)
        {
            return Result.Failure<PostLikeResponse>(FeedErrors.PostNotFound);
        }

        var alreadyLiked = await _unitOfWork.Repository<PostLike>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(item => item.PostId == request.PostId && item.UserId == request.UserId, cancellationToken);

        if (alreadyLiked)
        {
            return Result.Failure<PostLikeResponse>(FeedErrors.PostAlreadyLiked);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;

        await _unitOfWork.Repository<PostLike>().AddAsync(new PostLike
        {
            Id = Guid.CreateVersion7(),
            PostId = request.PostId,
            UserId = request.UserId,
            CreatedAt = now
        }, cancellationToken);

        post.LikesCount += 1;
        post.UpdatedAt = now;
        _unitOfWork.Repository<Post>().Update(post);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePostLikeViolation(exception))
        {
            post.LikesCount = Math.Max(0, post.LikesCount - 1);
            return Result.Failure<PostLikeResponse>(FeedErrors.PostAlreadyLiked);
        }

        await _feedNotificationService.NotifyPostLikedAsync(
            request.UserId,
            post.UserId,
            post.Id,
            FeedPostSupport.BuildPreview(post.Content),
            cancellationToken);

        return Result.Success(new PostLikeResponse(post.Id, post.LikesCount, true));
    }

    private static bool IsDuplicatePostLikeViolation(DbUpdateException exception)
    {
        var sqlState = exception.InnerException?.GetType().GetProperty("SqlState")?.GetValue(exception.InnerException) as string;
        var constraintName = exception.InnerException?.GetType().GetProperty("ConstraintName")?.GetValue(exception.InnerException) as string;

        if (sqlState == UniqueViolationSqlState
            && string.Equals(constraintName, PostLikeUniqueIndexName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return exception.InnerException?.Message.Contains(PostLikeUniqueIndexName, StringComparison.OrdinalIgnoreCase) == true
               || exception.Message.Contains(PostLikeUniqueIndexName, StringComparison.OrdinalIgnoreCase);
    }
}
