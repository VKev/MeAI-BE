using System.Text.Json;
using Application.Abstractions.Resources;
using Application.Chats.Models;
using Application.ChatSessions;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Chats.Queries;

public sealed record GetChatByIdQuery(Guid ChatId, Guid UserId) : IRequest<Result<ChatResponse>>;

public sealed class GetChatByIdQueryHandler
    : IRequestHandler<GetChatByIdQuery, Result<ChatResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IUserResourceService _userResourceService;

    public GetChatByIdQueryHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository,
        IUserResourceService userResourceService)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
        _userResourceService = userResourceService;
    }

    public async Task<Result<ChatResponse>> Handle(
        GetChatByIdQuery request,
        CancellationToken cancellationToken)
    {
        var chat = await _chatRepository.GetByIdAsync(request.ChatId, cancellationToken);
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

        var referenceIds = ParseResourceIds(chat.ReferenceResourceIds);
        var resultIds = ParseResourceIds(chat.ResultResourceIds);

        if (referenceIds.Count == 0 && resultIds.Count == 0)
        {
            return Result.Success(ChatMapping.ToResponse(chat));
        }

        var allIds = referenceIds.Concat(resultIds).Distinct().ToList();
        var presignResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            allIds,
            cancellationToken);

        if (presignResult.IsFailure)
        {
            return Result.Failure<ChatResponse>(presignResult.Error);
        }

        var urlById = presignResult.Value.ToDictionary(item => item.ResourceId, item => item.PresignedUrl);
        var referenceUrls = MapUrls(referenceIds, urlById);
        var resultUrls = MapUrls(resultIds, urlById);

        return Result.Success(ChatMapping.ToResponse(chat, referenceUrls, resultUrls));
    }

    private static List<Guid> ParseResourceIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<Guid>();
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            if (ids is null || ids.Count == 0)
            {
                return new List<Guid>();
            }

            return ids.Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
        }
        catch (JsonException)
        {
            try
            {
                var ids = JsonSerializer.Deserialize<List<Guid>>(json);
                return ids?.Where(id => id != Guid.Empty).ToList() ?? new List<Guid>();
            }
            catch (JsonException)
            {
                return new List<Guid>();
            }
        }
    }

    private static IReadOnlyList<string>? MapUrls(
        IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<Guid, string> urlById)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        var urls = new List<string>();
        foreach (var id in ids)
        {
            if (urlById.TryGetValue(id, out var url) && !string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url);
            }
        }

        return urls.Count == 0 ? null : urls;
    }
}
