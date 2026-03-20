using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetUserPostsQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IRequest<Result<IEnumerable<PostResponse>>>;

public sealed class GetUserPostsQueryHandler
    : IRequestHandler<GetUserPostsQuery, Result<IEnumerable<PostResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
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
        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);
        var posts = await _postRepository.GetByUserIdAsync(
            request.UserId,
            request.CursorCreatedAt,
            request.CursorId,
            pageSize,
            cancellationToken);

        var response = await _postResponseBuilder.BuildManyAsync(request.UserId, posts, cancellationToken);
        return Result.Success<IEnumerable<PostResponse>>(response);
    }
}
