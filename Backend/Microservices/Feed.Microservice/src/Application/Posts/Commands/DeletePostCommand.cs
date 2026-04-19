using Application.Abstractions.Data;
using Application.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record DeletePostCommand(Guid UserId, Guid PostId) : ICommand<bool>;

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

        if (post.UserId != request.UserId)
        {
            return Result.Failure<bool>(FeedErrors.Forbidden);
        }

        post.IsDeleted = true;
        post.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _unitOfWork.Repository<Post>().Update(post);

        var postHashtags = await _unitOfWork.Repository<PostHashtag>()
            .GetAll()
            .Where(item => item.PostId == post.Id)
            .ToListAsync(cancellationToken);

        if (postHashtags.Count > 0)
        {
            var hashtagIds = postHashtags.Select(item => item.HashtagId).Distinct().ToList();
            var hashtags = await _unitOfWork.Repository<Hashtag>()
                .GetAll()
                .Where(item => hashtagIds.Contains(item.Id))
                .ToListAsync(cancellationToken);

            foreach (var hashtag in hashtags)
            {
                hashtag.PostCount = Math.Max(0, hashtag.PostCount - postHashtags.Count(link => link.HashtagId == hashtag.Id));
                _unitOfWork.Repository<Hashtag>().Update(hashtag);
            }
        }

        return Result.Success(true);
    }
}
