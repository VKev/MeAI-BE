using Application.Abstractions.Agents;
using Application.Agents.Models;
using Application.ChatSessions;
using Domain.Entities;
using Domain.Repositories;
using System.Collections.Concurrent;
using MediatR;
using SharedLibrary.Extensions;
using SharedLibrary.Common.ResponseModel;

namespace Application.Agents.Commands;

public sealed record SendAgentMessageCommand(
    Guid SessionId,
    Guid UserId,
    string? Message,
    AgentImageOptions? ImageOptions = null,
    AgentScheduleOptions? ScheduleOptions = null) : IRequest<Result<AgentChatResponse>>;

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

        var assistantChatId = Guid.CreateVersion7();
        var completionResult = await _agentChatService.GenerateReplyAsync(
            new AgentChatRequest(
                request.UserId,
                session.Id,
                session.WorkspaceId,
                normalizedMessage,
                request.ImageOptions,
                request.ScheduleOptions,
                assistantChatId),
            cancellationToken);

        if (completionResult.IsFailure)
        {
            return Result.Failure<AgentChatResponse>(completionResult.Error);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var userChat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = session.Id,
            Prompt = normalizedMessage,
            Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(Role: "user")),
            Status = "completed",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _chatRepository.AddAsync(userChat, cancellationToken);

        Chat? assistantChat = null;
        var shouldPersistAssistant = !string.Equals(
            completionResult.Value.Action,
            "validation_failed",
            StringComparison.Ordinal);

        if (shouldPersistAssistant)
        {
            assistantChat = new Chat
            {
                Id = assistantChatId,
                SessionId = session.Id,
                Prompt = completionResult.Value.Content,
                Config = AgentMessageConfigSerializer.Serialize(new AgentChatMetadata(
                    Role: "assistant",
                    Model: completionResult.Value.Model,
                    ToolNames: completionResult.Value.ToolNames,
                    Actions: completionResult.Value.Actions,
                    RetrievalMode: completionResult.Value.RetrievalMode,
                    SourceUrls: completionResult.Value.SourceUrls,
                    ImportedResourceIds: completionResult.Value.ImportedResourceIds)),
                Status = "completed",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _chatRepository.AddAsync(assistantChat, cancellationToken);
        }

        await _chatRepository.SaveChangesAsync(cancellationToken);

        var userMessage = AgentMessageConfigSerializer.ToResponse(userChat);
        var assistantMessage = shouldPersistAssistant && assistantChat is not null
            ? AgentMessageConfigSerializer.ToResponse(assistantChat)
            : new AgentMessageResponse(
                Guid.CreateVersion7(),
                session.Id,
                "assistant",
                completionResult.Value.Content,
                "completed",
                null,
                completionResult.Value.Model,
                completionResult.Value.ToolNames,
                completionResult.Value.Actions?.ToArray() ?? [],
                completionResult.Value.RetrievalMode,
                completionResult.Value.SourceUrls?.ToArray() ?? [],
                completionResult.Value.ImportedResourceIds?.ToArray() ?? [],
                DateTime.UtcNow,
                null);

        return Result.Success(new AgentChatResponse(
            session.Id,
            userMessage,
            assistantMessage,
            completionResult.Value.Action,
            completionResult.Value.ValidationError,
            completionResult.Value.RevisedPrompt,
            completionResult.Value.PostId,
            completionResult.Value.ScheduleId,
            completionResult.Value.ChatId,
            completionResult.Value.CorrelationId,
            completionResult.Value.RetrievalMode,
            completionResult.Value.SourceUrls,
            completionResult.Value.ImportedResourceIds));
    }

    private static string NormalizeMessage(string? message)
    {
        return string.Join(' ', (message ?? string.Empty)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
