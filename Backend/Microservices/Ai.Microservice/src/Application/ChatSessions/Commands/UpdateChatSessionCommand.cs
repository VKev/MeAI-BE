using Application.ChatSessions.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.ChatSessions.Commands;

public sealed record UpdateChatSessionCommand(
    Guid ChatSessionId,
    Guid UserId,
    string? SessionName) : IRequest<Result<ChatSessionResponse>>;

public sealed class UpdateChatSessionCommandHandler
    : IRequestHandler<UpdateChatSessionCommand, Result<ChatSessionResponse>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public UpdateChatSessionCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<ChatSessionResponse>> Handle(
        UpdateChatSessionCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _chatSessionRepository.GetByIdForUpdateAsync(request.ChatSessionId, cancellationToken);

        if (session == null || session.DeletedAt.HasValue)
        {
            return Result.Failure<ChatSessionResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatSessionResponse>(ChatSessionErrors.Unauthorized);
        }

        session.SessionName = NormalizeString(request.SessionName);
        session.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _chatSessionRepository.Update(session);
        await _chatSessionRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ChatSessionMapping.ToResponse(session));
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
