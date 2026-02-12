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
    Guid? ChatSessionId,
    Guid? WorkspaceId,
    string? Prompt,
    string? Config,
    IReadOnlyList<Guid>? ReferenceResourceIds) : IRequest<Result<ChatResponse>>;

public sealed class CreateChatCommandHandler
    : IRequestHandler<CreateChatCommand, Result<ChatResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IWorkspaceRepository _workspaceRepository;

    public CreateChatCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository,
        IWorkspaceRepository workspaceRepository)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Result<ChatResponse>> Handle(
        CreateChatCommand request,
        CancellationToken cancellationToken)
    {
        var sessionResult = await ResolveSessionAsync(request, cancellationToken);
        if (sessionResult.IsFailure)
        {
            return Result.Failure<ChatResponse>(sessionResult.Error);
        }

        var session = sessionResult.Value;

        var referenceIds = NormalizeIds(request.ReferenceResourceIds);
        var chat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = session.Id,
            Prompt = request.Prompt?.Trim(),
            Config = string.IsNullOrWhiteSpace(request.Config) ? null : request.Config.Trim(),
            ReferenceResourceIds = referenceIds.Count == 0 ? null : JsonSerializer.Serialize(referenceIds),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatRepository.AddAsync(chat, cancellationToken);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ChatMapping.ToResponse(chat));
    }

    private async Task<Result<ChatSession>> ResolveSessionAsync(
        CreateChatCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ChatSessionId.HasValue)
        {
            var existingSession = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId.Value, cancellationToken);
            if (existingSession is null || existingSession.DeletedAt.HasValue)
            {
                return Result.Failure<ChatSession>(ChatSessionErrors.NotFound);
            }

            if (existingSession.UserId != request.UserId)
            {
                return Result.Failure<ChatSession>(ChatSessionErrors.Unauthorized);
            }

            if (request.WorkspaceId.HasValue && existingSession.WorkspaceId != request.WorkspaceId.Value)
            {
                return Result.Failure<ChatSession>(ChatSessionErrors.WorkspaceMismatch);
            }

            return Result.Success(existingSession);
        }

        if (!request.WorkspaceId.HasValue)
        {
            return Result.Failure<ChatSession>(ChatSessionErrors.WorkspaceIdRequired);
        }

        var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
            request.WorkspaceId.Value,
            request.UserId,
            cancellationToken);

        if (!workspaceExists)
        {
            return Result.Failure<ChatSession>(ChatSessionErrors.WorkspaceNotFound);
        }

        var newSession = new ChatSession
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId.Value,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatSessionRepository.AddAsync(newSession, cancellationToken);
        await _chatSessionRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(newSession);
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
