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

public sealed record CreateCommentCommand(
    Guid UserId,
    Guid PostId,
    string Content) : ICommand<CommentResponse>;

public sealed class CreateCommentCommandHandler : ICommandHandler<CreateCommentCommand, CommentResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeedNotificationService _feedNotificationService;

    public CreateCommentCommandHandler(
        IUnitOfWork unitOfWork,
        IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<CommentResponse>> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var content = FeedPostSupport.NormalizeOptionalText(request.Content);
        if (content is null)
        {
            return Result.Failure<CommentResponse>(FeedErrors.EmptyComment);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<CommentResponse>(FeedErrors.PostNotFound);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var comment = new Comment
        {
            Id = Guid.CreateVersion7(),
            PostId = request.PostId,
            UserId = request.UserId,
            ParentCommentId = null,
            Content = content,
            LikesCount = 0,
            RepliesCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _unitOfWork.Repository<Comment>().AddAsync(comment, cancellationToken);

        post.CommentsCount += 1;
        post.UpdatedAt = now;
        _unitOfWork.Repository<Post>().Update(post);

        await _feedNotificationService.NotifyCommentAsync(
            request.UserId,
            post.UserId,
            post.Id,
            comment.Id,
            FeedPostSupport.BuildPreview(content),
            cancellationToken);

        return Result.Success(CommentResponseMapping.ToResponse(comment));
    }
}
