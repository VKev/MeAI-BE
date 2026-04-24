using Application.Abstractions.Agents;
using Application.Agents.Models;
using Application.ChatSessions;
using System.Collections.Concurrent;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Agents.Commands;

public sealed record SendAgentMessageCommand(
    Guid SessionId,
    Guid UserId,
    string? Message) : IRequest<Result<AgentChatResponse>>;

public sealed class SendAgentMessageCommandHandler
    : IRequestHandler<SendAgentMessageCommand, Result<AgentChatResponse>>
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SessionLocks = new();

    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatRepository _chatRepository;
    private readonly IAgentChatService _agentChatService;

    public SendAgentMessageCommandHandler(
        IChatSessionRepository chatSessionRepository,
        IChatRepository chatRepository,
        IAgentChatService agentChatService)
    {
        _chatSessionRepository = chatSessionRepository;
        _chatRepository = chatRepository;
        _agentChatService = agentChatService;
    }

    public async Task<Result<AgentChatResponse>> Handle(
        SendAgentMessageCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedMessage = NormalizeMessage(request.Message);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return Result.Failure<AgentChatResponse>(AgentErrors.InvalidMessage);
        }

        var sessionLock = SessionLocks.GetOrAdd(request.SessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            return await HandleLockedAsync(request, normalizedMessage, cancellationToken);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private async Task<Result<AgentChatResponse>> HandleLockedAsync(
        SendAgentMessageCommand request,
        string normalizedMessage,
        CancellationToken cancellationToken)
    {
        var session = await _chatSessionRepository.GetByIdForUpdateAsync(request.SessionId, cancellationToken);
        if (session is null || session.DeletedAt.HasValue)
        {
            return Result.Failure<AgentChatResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<AgentChatResponse>(ChatSessionErrors.Unauthorized);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var existingChats = (await _chatRepository.GetBySessionIdAsync(session.Id, cancellationToken))
            .OrderBy(chat => chat.CreatedAt ?? DateTime.MinValue)
            .ThenBy(chat => chat.Id)
            .ToList();

        var duplicateResult = TryHandleDuplicateMessage(session.Id, normalizedMessage, existingChats, now);
        if (duplicateResult is not null)
        {
            return duplicateResult;
        }

        var userChat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = session.Id,
            Prompt = normalizedMessage,
            Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(Role: "user")),
            CreatedAt = now
        };

        await _chatRepository.AddAsync(userChat, cancellationToken);
        session.UpdatedAt = now;
        _chatSessionRepository.Update(session);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        var completionResult = await _agentChatService.GenerateReplyAsync(
            new AgentChatRequest(request.UserId, session.Id, session.WorkspaceId),
            cancellationToken);

        if (completionResult.IsFailure)
        {
            return Result.Failure<AgentChatResponse>(completionResult.Error);
        }

        var assistantChat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = session.Id,
            Prompt = completionResult.Value.Content,
            Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(
                Role: "assistant",
                Model: completionResult.Value.Model,
                ToolNames: completionResult.Value.ToolNames,
                Actions: completionResult.Value.Actions)),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatRepository.AddAsync(assistantChat, cancellationToken);
        session.UpdatedAt = assistantChat.CreatedAt;
        _chatSessionRepository.Update(session);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new AgentChatResponse(
            session.Id,
            AgentMessageConfigSerializer.ToResponse(userChat),
            AgentMessageConfigSerializer.ToResponse(assistantChat)));
    }

    private static Result<AgentChatResponse>? TryHandleDuplicateMessage(
        Guid sessionId,
        string normalizedMessage,
        IReadOnlyList<Chat> existingChats,
        DateTime now)
    {
        if (existingChats.Count == 0)
        {
            return null;
        }

        var latest = existingChats[^1];
        var latestMetadata = AgentMessageConfigSerializer.Parse(latest.Config);

        if (string.Equals(latestMetadata.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            IsSameRecentMessage(latest, normalizedMessage, now, TimeSpan.FromSeconds(30)))
        {
            return Result.Failure<AgentChatResponse>(AgentErrors.DuplicateMessageInProgress);
        }

        if (!string.Equals(latestMetadata.Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var previousUser = existingChats
            .Take(existingChats.Count - 1)
            .LastOrDefault(chat =>
                string.Equals(
                    AgentMessageConfigSerializer.Parse(chat.Config).Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase));

        if (previousUser is null ||
            !IsSameRecentMessage(previousUser, normalizedMessage, now, TimeSpan.FromMinutes(5)))
        {
            return null;
        }

        return Result.Success(new AgentChatResponse(
            sessionId,
            AgentMessageConfigSerializer.ToResponse(previousUser),
            AgentMessageConfigSerializer.ToResponse(latest)));
    }

    private static bool IsSameRecentMessage(Chat chat, string normalizedMessage, DateTime now, TimeSpan window)
    {
        if (!string.Equals(NormalizeMessage(chat.Prompt), normalizedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!chat.CreatedAt.HasValue)
        {
            return true;
        }

        var age = now - chat.CreatedAt.Value;
        return age >= TimeSpan.Zero && age <= window;
    }

    private static string NormalizeMessage(string? message)
    {
        return string.Join(' ', (message ?? string.Empty)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
