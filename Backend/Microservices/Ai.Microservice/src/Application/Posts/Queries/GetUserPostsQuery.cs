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

    public GetUserPostsQueryHandler(IPostRepository postRepository)
    {
        _postRepository = postRepository;
    }

    public async Task<Result<IEnumerable<PostResponse>>> Handle(
        GetUserPostsQuery request,
        CancellationToken cancellationToken)
    {
        var posts = await _postRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var response = posts
            .Where(post => !post.DeletedAt.HasValue)
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Select(PostMapping.ToResponse)
            .ToList();

        return Result.Success<IEnumerable<PostResponse>>(response);
    }
}
