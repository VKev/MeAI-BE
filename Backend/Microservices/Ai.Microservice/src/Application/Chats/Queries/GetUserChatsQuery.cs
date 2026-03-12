using System.Text.Json;
using Application.Abstractions.Resources;
using Application.Chats.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Chats.Queries;

public sealed record GetUserChatsQuery(Guid UserId) : IRequest<Result<IEnumerable<ChatResponse>>>;

public sealed class GetUserChatsQueryHandler
    : IRequestHandler<GetUserChatsQuery, Result<IEnumerable<ChatResponse>>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IUserResourceService _userResourceService;

    public GetUserChatsQueryHandler(
        IChatRepository chatRepository,
        IUserResourceService userResourceService)
    {
        _chatRepository = chatRepository;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IEnumerable<ChatResponse>>> Handle(
        GetUserChatsQuery request,
        CancellationToken cancellationToken)
    {
        var chats = await _chatRepository.GetByUserIdAsync(request.UserId, cancellationToken);
        var chatList = chats
            .Where(chat => !chat.DeletedAt.HasValue)
            .OrderByDescending(chat => chat.CreatedAt)
            .ThenByDescending(chat => chat.Id)
            .ToList();

        if (chatList.Count == 0)
        {
            return Result.Success<IEnumerable<ChatResponse>>(Array.Empty<ChatResponse>());
        }

        var chatItems = chatList
            .Select(chat => (
                Chat: chat,
                ReferenceIds: ParseResourceIds(chat.ReferenceResourceIds),
                ResultIds: ParseResourceIds(chat.ResultResourceIds)))
            .ToList();

        var allIds = chatItems
            .SelectMany(item => item.ReferenceIds.Concat(item.ResultIds))
            .Distinct()
            .ToList();

        IReadOnlyDictionary<Guid, string> urlById = new Dictionary<Guid, string>();
        if (allIds.Count > 0)
        {
            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                allIds,
                cancellationToken);

            if (presignResult.IsFailure)
            {
                return Result.Failure<IEnumerable<ChatResponse>>(presignResult.Error);
            }

            urlById = presignResult.Value.ToDictionary(item => item.ResourceId, item => item.PresignedUrl);
        }

        var response = chatItems
            .Select(item => ChatMapping.ToResponse(
                item.Chat,
                MapUrls(item.ReferenceIds, urlById),
                MapUrls(item.ResultIds, urlById)))
            .ToList();

        return Result.Success<IEnumerable<ChatResponse>>(response);
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
