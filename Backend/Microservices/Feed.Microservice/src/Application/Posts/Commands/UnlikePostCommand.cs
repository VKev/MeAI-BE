using Application.Abstractions.Data;
using Application.Common;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record UnlikePostCommand(Guid UserId, Guid PostId) : ICommand<PostLikeResponse>;

public sealed class UnlikePostCommandHandler : ICommandHandler<UnlikePostCommand, PostLikeResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public UnlikePostCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PostLikeResponse>> Handle(UnlikePostCommand request, CancellationToken cancellationToken)
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

        var postLike = await _unitOfWork.Repository<PostLike>()
            .GetAll()
            .FirstOrDefaultAsync(
                item => item.PostId == request.PostId && item.UserId == request.UserId,
                cancellationToken);

        if (postLike is null)
        {
            return Result.Failure<PostLikeResponse>(FeedErrors.PostNotLiked);
        }

        _unitOfWork.Repository<PostLike>().Delete(postLike);

        post.LikesCount = Math.Max(0, post.LikesCount - 1);
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _unitOfWork.Repository<Post>().Update(post);

        return Result.Success(new PostLikeResponse(post.Id, post.LikesCount, false));
    }
}
