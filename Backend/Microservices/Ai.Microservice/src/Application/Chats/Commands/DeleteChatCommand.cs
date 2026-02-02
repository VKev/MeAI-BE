using Application.ChatSessions;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record DeleteChatCommand(Guid ChatId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteChatCommandHandler
    : IRequestHandler<DeleteChatCommand, Result<bool>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;

    public DeleteChatCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> Handle(
        DeleteChatCommand request,
        CancellationToken cancellationToken)
    {
        var chat = await _chatRepository.GetByIdForUpdateAsync(request.ChatId, cancellationToken);
        if (chat is null || chat.DeletedAt.HasValue)
        {
            return Result.Failure<bool>(ChatErrors.NotFound);
        }

        var session = await _chatSessionRepository.GetByIdAsync(chat.SessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<bool>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<bool>(ChatErrors.Unauthorized);
        }

        chat.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _chatRepository.Update(chat);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
