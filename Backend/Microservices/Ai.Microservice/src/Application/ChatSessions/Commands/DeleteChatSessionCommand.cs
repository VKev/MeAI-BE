using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.ChatSessions.Commands;

public sealed record DeleteChatSessionCommand(Guid ChatSessionId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteChatSessionCommandHandler
    : IRequestHandler<DeleteChatSessionCommand, Result<bool>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public DeleteChatSessionCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> Handle(
        DeleteChatSessionCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _chatSessionRepository.GetByIdForUpdateAsync(request.ChatSessionId, cancellationToken);

        if (session == null || session.DeletedAt.HasValue)
        {
            return Result.Failure<bool>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<bool>(ChatSessionErrors.Unauthorized);
        }

        session.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        session.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _chatSessionRepository.Update(session);
        await _chatSessionRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
