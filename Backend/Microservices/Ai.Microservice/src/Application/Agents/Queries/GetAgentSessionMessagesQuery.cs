using Application.Agents.Models;
using Application.ChatSessions;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Agents.Queries;

public sealed record GetAgentSessionMessagesQuery(
    Guid SessionId,
    Guid UserId) : IRequest<Result<IReadOnlyList<AgentMessageResponse>>>;

public sealed class GetAgentSessionMessagesQueryHandler
    : IRequestHandler<GetAgentSessionMessagesQuery, Result<IReadOnlyList<AgentMessageResponse>>>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatRepository _chatRepository;

    public GetAgentSessionMessagesQueryHandler(
        IChatSessionRepository chatSessionRepository,
        IChatRepository chatRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _chatRepository = chatRepository;
    }

    public async Task<Result<IReadOnlyList<AgentMessageResponse>>> Handle(
        GetAgentSessionMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var session = await _chatSessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null || session.DeletedAt.HasValue)
        {
            return Result.Failure<IReadOnlyList<AgentMessageResponse>>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<IReadOnlyList<AgentMessageResponse>>(ChatSessionErrors.Unauthorized);
        }

        var chats = await _chatRepository.GetBySessionIdAsync(request.SessionId, cancellationToken);
        var response = chats
            .Where(chat => !chat.DeletedAt.HasValue)
            .OrderBy(chat => chat.CreatedAt ?? DateTime.MinValue)
            .ThenBy(chat => chat.Id)
            .Select(AgentMessageConfigSerializer.ToResponse)
            .ToList();

        return Result.Success<IReadOnlyList<AgentMessageResponse>>(response);
    }
}
