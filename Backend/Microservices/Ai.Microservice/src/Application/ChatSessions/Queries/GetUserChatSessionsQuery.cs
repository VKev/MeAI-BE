using Application.ChatSessions.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.ChatSessions.Queries;

public sealed record GetUserChatSessionsQuery(Guid UserId) : IRequest<Result<IEnumerable<ChatSessionResponse>>>;

public sealed class GetUserChatSessionsQueryHandler
    : IRequestHandler<GetUserChatSessionsQuery, Result<IEnumerable<ChatSessionResponse>>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetUserChatSessionsQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IEnumerable<ChatSessionResponse>>> Handle(
        GetUserChatSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var sessions = await _chatSessionRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var response = sessions
            .Where(session => !session.DeletedAt.HasValue)
            .OrderByDescending(session => session.CreatedAt)
            .ThenByDescending(session => session.Id)
            .Select(ChatSessionMapping.ToResponse)
            .ToList();

        return Result.Success<IEnumerable<ChatSessionResponse>>(response);
    }
}
