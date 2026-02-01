using Application.ChatSessions.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.ChatSessions.Queries;

public sealed record GetChatSessionByIdQuery(Guid ChatSessionId, Guid UserId) : IRequest<Result<ChatSessionResponse>>;

public sealed class GetChatSessionByIdQueryHandler
    : IRequestHandler<GetChatSessionByIdQuery, Result<ChatSessionResponse>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetChatSessionByIdQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<ChatSessionResponse>> Handle(
        GetChatSessionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var session = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);

        if (session == null || session.DeletedAt.HasValue)
        {
            return Result.Failure<ChatSessionResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatSessionResponse>(ChatSessionErrors.Unauthorized);
        }

        return Result.Success(ChatSessionMapping.ToResponse(session));
    }
}
