using Application.ChatSessions.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.ChatSessions.Commands;

public sealed record CreateChatSessionCommand(
    Guid UserId,
    string? SessionName) : IRequest<Result<ChatSessionResponse>>;

public sealed class CreateChatSessionCommandHandler
    : IRequestHandler<CreateChatSessionCommand, Result<ChatSessionResponse>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public CreateChatSessionCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<ChatSessionResponse>> Handle(
        CreateChatSessionCommand request,
        CancellationToken cancellationToken)
    {
        var session = new ChatSession
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            SessionName = NormalizeString(request.SessionName),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatSessionRepository.AddAsync(session, cancellationToken);
        await _chatSessionRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ChatSessionMapping.ToResponse(session));
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
