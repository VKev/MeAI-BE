using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetWorkspacePostsQuery(Guid WorkspaceId, Guid UserId) : IRequest<Result<IEnumerable<PostResponse>>>;

public sealed class GetWorkspacePostsQueryHandler
    : IRequestHandler<GetWorkspacePostsQuery, Result<IEnumerable<PostResponse>>>
{
    private readonly IPostRepository _postRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public GetWorkspacePostsQueryHandler(
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _workspaceRepository = workspaceRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<IEnumerable<PostResponse>>> Handle(
        GetWorkspacePostsQuery request,
        CancellationToken cancellationToken)
    {
        var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
            request.WorkspaceId,
            request.UserId,
            cancellationToken);

        if (!workspaceExists)
        {
            return Result.Failure<IEnumerable<PostResponse>>(PostErrors.WorkspaceNotFound);
        }

        var posts = await _postRepository.GetByUserIdAndWorkspaceIdAsync(
            request.UserId,
            request.WorkspaceId,
            cancellationToken);

        var filteredPosts = posts
            .Where(post => !post.DeletedAt.HasValue)
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .ToList();

        var response = await _postResponseBuilder.BuildManyAsync(request.UserId, filteredPosts, cancellationToken);
        return Result.Success<IEnumerable<PostResponse>>(response);
    }
}
