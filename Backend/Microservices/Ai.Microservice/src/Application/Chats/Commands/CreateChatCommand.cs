using System.Text.Json;
using Application.ChatSessions;
using Application.Chats.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record CreateChatCommand(
    Guid UserId,
    Guid ChatSessionId,
    string? Prompt,
    string? Config,
    IReadOnlyList<Guid>? ReferenceResourceIds) : IRequest<Result<ChatResponse>>;

public sealed class CreateChatCommandHandler
    : IRequestHandler<CreateChatCommand, Result<ChatResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;

    public CreateChatCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<ChatResponse>> Handle(
        CreateChatCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ChatResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatResponse>(ChatSessionErrors.Unauthorized);
        }

        var referenceIds = NormalizeIds(request.ReferenceResourceIds);
        var chat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = request.ChatSessionId,
            Prompt = request.Prompt?.Trim(),
            Config = string.IsNullOrWhiteSpace(request.Config) ? null : request.Config.Trim(),
            ReferenceResourceIds = referenceIds.Count == 0 ? null : JsonSerializer.Serialize(referenceIds),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatRepository.AddAsync(chat, cancellationToken);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ChatMapping.ToResponse(chat));
    }

    private static List<string> NormalizeIds(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return new List<string>();
        }

        return ids.Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => id.ToString())
            .ToList();
    }
}
