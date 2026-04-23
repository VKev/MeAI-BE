using Application.Abstractions.Agents;
using Application.Agents.Models;
using Application.ChatSessions;
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
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Result.Failure<AgentChatResponse>(AgentErrors.InvalidMessage);
        }

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
        var userChat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = session.Id,
            Prompt = request.Message.Trim(),
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
                ToolNames: completionResult.Value.ToolNames)),
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
}
