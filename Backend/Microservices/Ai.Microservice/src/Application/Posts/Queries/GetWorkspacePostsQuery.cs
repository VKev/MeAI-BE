using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetWorkspacePostsQuery(
    Guid WorkspaceId,
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    string? Status = null) : IRequest<Result<IEnumerable<PostResponse>>>;

public sealed class GetWorkspacePostsQueryHandler
    : IRequestHandler<GetWorkspacePostsQuery, Result<IEnumerable<PostResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
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

        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);
        var posts = await _postRepository.GetByUserIdAndWorkspaceIdAsync(
            request.UserId,
            request.WorkspaceId,
            request.CursorCreatedAt,
            request.CursorId,
            pageSize,
            request.Status,
            cancellationToken);

        var response = await _postResponseBuilder.BuildManyAsync(request.UserId, posts, cancellationToken);
        return Result.Success<IEnumerable<PostResponse>>(response);
    }
}
