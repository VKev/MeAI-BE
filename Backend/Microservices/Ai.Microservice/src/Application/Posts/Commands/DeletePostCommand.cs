using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record DeletePostCommand(Guid PostId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeletePostCommandHandler : IRequestHandler<DeletePostCommand, Result<bool>>
{
    private readonly IPostRepository _postRepository;

    public DeletePostCommandHandler(IPostRepository postRepository)
    {
        _postRepository = postRepository;
    }

    public async Task<Result<bool>> Handle(DeletePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);

        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<bool>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<bool>(PostErrors.Unauthorized);
        }

        post.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
