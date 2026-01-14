using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetPostByIdQuery(Guid PostId, Guid UserId) : IRequest<Result<PostResponse>>;

public sealed class GetPostByIdQueryHandler
    : IRequestHandler<GetPostByIdQuery, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;

    public GetPostByIdQueryHandler(IPostRepository postRepository)
    {
        _postRepository = postRepository;
    }

    public async Task<Result<PostResponse>> Handle(GetPostByIdQuery request, CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdAsync(request.PostId, cancellationToken);

        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<PostResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<PostResponse>(PostErrors.Unauthorized);
        }

        return Result.Success(PostMapping.ToResponse(post));
    }
}
