using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Common;
using Application.Comments.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Comments.Commands;

public sealed record ReplyToCommentCommand(
    Guid UserId,
    Guid CommentId,
    string Content) : ICommand<CommentResponse>;

public sealed class ReplyToCommentCommandHandler : ICommandHandler<ReplyToCommentCommand, CommentResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeedNotificationService _feedNotificationService;

    public ReplyToCommentCommandHandler(
        IUnitOfWork unitOfWork,
        IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<CommentResponse>> Handle(ReplyToCommentCommand request, CancellationToken cancellationToken)
    {
        var content = FeedPostSupport.NormalizeOptionalText(request.Content);
        if (content is null)
        {
            return Result.Failure<CommentResponse>(FeedErrors.EmptyComment);
        }

        var parentComment = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.CommentId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (parentComment is null)
        {
            return Result.Failure<CommentResponse>(FeedErrors.CommentNotFound);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == parentComment.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<CommentResponse>(FeedErrors.PostNotFound);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var reply = new Comment
        {
            Id = Guid.CreateVersion7(),
            PostId = parentComment.PostId,
            UserId = request.UserId,
            ParentCommentId = parentComment.Id,
            Content = content,
            LikesCount = 0,
            RepliesCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _unitOfWork.Repository<Comment>().AddAsync(reply, cancellationToken);

        parentComment.RepliesCount += 1;
        parentComment.UpdatedAt = now;
        _unitOfWork.Repository<Comment>().Update(parentComment);

        post.CommentsCount += 1;
        post.UpdatedAt = now;
        _unitOfWork.Repository<Post>().Update(post);

        await _feedNotificationService.NotifyCommentAsync(
            request.UserId,
            post.UserId,
            post.Id,
            reply.Id,
            FeedPostSupport.BuildPreview(content),
            cancellationToken);

        return Result.Success(CommentResponseMapping.ToResponse(reply, post.UserId == request.UserId));
    }
}
