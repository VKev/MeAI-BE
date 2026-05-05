using Application.ChatSessions;
using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetChatSessionPostsQuery(
    Guid ChatSessionId,
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    string? Status = null,
    Guid? SocialMediaId = null,
    string? Platform = null) : IRequest<Result<IEnumerable<PostResponse>>>;

public sealed class GetChatSessionPostsQueryHandler
    : IRequestHandler<GetChatSessionPostsQuery, Result<IEnumerable<PostResponse>>>
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPostRepository _postRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public GetChatSessionPostsQueryHandler(
        IChatSessionRepository chatSessionRepository,
        IPostRepository postRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _chatSessionRepository = chatSessionRepository;
        _postRepository = postRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<IEnumerable<PostResponse>>> Handle(
        GetChatSessionPostsQuery request,
        CancellationToken cancellationToken)
    {
        var chatSession = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);
        if (chatSession is null || chatSession.DeletedAt.HasValue)
        {
            return Result.Failure<IEnumerable<PostResponse>>(ChatSessionErrors.NotFound);
        }

        if (chatSession.UserId != request.UserId)
        {
            return Result.Failure<IEnumerable<PostResponse>>(ChatSessionErrors.Unauthorized);
        }

        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);
        var posts = await _postRepository.GetByUserIdAndChatSessionIdAsync(
            request.UserId,
            request.ChatSessionId,
            request.CursorCreatedAt,
            request.CursorId,
            limit,
            request.Status,
            request.SocialMediaId,
            request.Platform,
            cancellationToken);

        var response = await _postResponseBuilder.BuildManyAsync(request.UserId, posts, cancellationToken);
        return Result.Success<IEnumerable<PostResponse>>(response);
    }
}
