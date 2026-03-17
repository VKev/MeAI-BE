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
    private readonly PostResponseBuilder _postResponseBuilder;

    public GetPostByIdQueryHandler(IPostRepository postRepository, PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _postResponseBuilder = postResponseBuilder;
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

        var response = await _postResponseBuilder.BuildAsync(request.UserId, post, cancellationToken);
        return Result.Success(response);
    }
}
