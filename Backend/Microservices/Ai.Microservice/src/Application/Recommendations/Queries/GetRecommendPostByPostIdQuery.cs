using Application.Posts;
using Application.Recommendations.Commands;
using Application.Recommendations.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Queries;

/// <summary>
/// GET status / result of an improve-post task by the original post id. Returns
/// <c>RecommendPost.NotFound</c> when no improvement has ever been requested for
/// the given post (or when the most recent one was deleted under replace-on-rerun
/// before this query landed — which shouldn't happen in practice given the FE
/// usually polls a known correlation id).
/// </summary>
public sealed record GetRecommendPostByPostIdQuery(
    Guid UserId,
    Guid OriginalPostId) : IRequest<Result<RecommendPostTaskResponse>>;

public sealed class GetRecommendPostByPostIdQueryHandler
    : IRequestHandler<GetRecommendPostByPostIdQuery, Result<RecommendPostTaskResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IRecommendPostRepository _recommendPostRepository;

    public GetRecommendPostByPostIdQueryHandler(
        IPostRepository postRepository,
        IRecommendPostRepository recommendPostRepository)
    {
        _postRepository = postRepository;
        _recommendPostRepository = recommendPostRepository;
    }

    public async Task<Result<RecommendPostTaskResponse>> Handle(
        GetRecommendPostByPostIdQuery request,
        CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdAsync(request.OriginalPostId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<RecommendPostTaskResponse>(PostErrors.NotFound);
        }
        if (post.UserId != request.UserId)
        {
            return Result.Failure<RecommendPostTaskResponse>(PostErrors.Unauthorized);
        }

        var entity = await _recommendPostRepository.GetByOriginalPostIdAsync(
            request.OriginalPostId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<RecommendPostTaskResponse>(
                new Error(
                    "RecommendPost.NotFound",
                    "No improve-post task has been started for this post yet."));
        }

        return Result.Success(StartImprovePostCommandHandler.MapToResponse(entity));
    }
}
