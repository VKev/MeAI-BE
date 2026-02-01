using System.Text.Json;
using Application.ChatSessions;
using Application.Chats.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record UpdateChatCommand(
    Guid ChatId,
    Guid UserId,
    string? Prompt,
    string? Config,
    IReadOnlyList<Guid>? ReferenceResourceIds) : IRequest<Result<ChatResponse>>;

public sealed class UpdateChatCommandHandler
    : IRequestHandler<UpdateChatCommand, Result<ChatResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;

    public UpdateChatCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<ChatResponse>> Handle(
        UpdateChatCommand request,
        CancellationToken cancellationToken)
    {
        var chat = await _chatRepository.GetByIdForUpdateAsync(request.ChatId, cancellationToken);
        if (chat is null || chat.DeletedAt.HasValue)
        {
            return Result.Failure<ChatResponse>(ChatErrors.NotFound);
        }

        var session = await _chatSessionRepository.GetByIdAsync(chat.SessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ChatResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatResponse>(ChatErrors.Unauthorized);
        }

        if (request.Prompt is not null)
        {
            chat.Prompt = request.Prompt.Trim();
        }

        if (request.Config is not null)
        {
            chat.Config = string.IsNullOrWhiteSpace(request.Config) ? null : request.Config.Trim();
        }

        if (request.ReferenceResourceIds is not null)
        {
            var referenceIds = request.ReferenceResourceIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Select(id => id.ToString())
                .ToList();
            chat.ReferenceResourceIds = referenceIds.Count == 0 ? null : JsonSerializer.Serialize(referenceIds);
        }

        chat.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _chatRepository.Update(chat);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ChatMapping.ToResponse(chat));
    }
}
