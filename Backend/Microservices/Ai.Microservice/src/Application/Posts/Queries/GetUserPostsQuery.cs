using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetUserPostsQuery(Guid UserId) : IRequest<Result<IEnumerable<PostResponse>>>;

public sealed class GetUserPostsQueryHandler
    : IRequestHandler<GetUserPostsQuery, Result<IEnumerable<PostResponse>>>
{
    private readonly IPostRepository _postRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public GetUserPostsQueryHandler(IPostRepository postRepository, PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<IEnumerable<PostResponse>>> Handle(
        GetUserPostsQuery request,
        CancellationToken cancellationToken)
    {
        var posts = await _postRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var filteredPosts = posts
            .Where(post => !post.DeletedAt.HasValue)
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .ToList();

        var response = await _postResponseBuilder.BuildManyAsync(request.UserId, filteredPosts, cancellationToken);
        return Result.Success<IEnumerable<PostResponse>>(response);
    }
}
