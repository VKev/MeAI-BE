using Application.Abstractions.Resources;
using Application.Chats.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Chats.Queries;

public sealed record GetAllAiResourcesQuery(
    Guid UserId,
    IReadOnlyList<string>? ResourceTypes)
    : IRequest<Result<IEnumerable<WorkspaceAiResourceResponse>>>;

public sealed class GetAllAiResourcesQueryHandler
    : IRequestHandler<GetAllAiResourcesQuery, Result<IEnumerable<WorkspaceAiResourceResponse>>>
{
    private static readonly string[] AllowedTypes = ["image", "video"];
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatRepository _chatRepository;
    private readonly IUserResourceService _userResourceService;

    public GetAllAiResourcesQueryHandler(
        IChatSessionRepository chatSessionRepository,
        IChatRepository chatRepository,
        IUserResourceService userResourceService)
    {
        _chatSessionRepository = chatSessionRepository;
        _chatRepository = chatRepository;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IEnumerable<WorkspaceAiResourceResponse>>> Handle(
        GetAllAiResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var selectedTypes = NormalizeTypes(request.ResourceTypes);
        var sessions = (await _chatSessionRepository.GetByUserIdAsync(request.UserId, cancellationToken))
            .Where(session => !session.DeletedAt.HasValue)
            .ToList();

        if (sessions.Count == 0)
        {
            return Result.Success<IEnumerable<WorkspaceAiResourceResponse>>(Array.Empty<WorkspaceAiResourceResponse>());
        }

        var sessionIds = sessions.Select(session => session.Id).ToList();
        var chats = (await _chatRepository.GetBySessionIdsAsync(sessionIds, cancellationToken))
            .Where(chat => !chat.DeletedAt.HasValue)
            .ToList();

        var resourcePairs = chats
            .SelectMany(chat => ChatResourceIdParser.Parse(chat.ResultResourceIds)
                .Select(resourceId => new ChatResourcePair(chat.SessionId, chat.Id, resourceId, chat.CreatedAt)))
            .DistinctBy(pair => new { pair.ChatId, pair.ResourceId })
            .ToList();

        if (resourcePairs.Count == 0)
        {
            return Result.Success<IEnumerable<WorkspaceAiResourceResponse>>(Array.Empty<WorkspaceAiResourceResponse>());
        }

        var resourceIds = resourcePairs.Select(pair => pair.ResourceId).Distinct().ToList();
        var presignResult = await _userResourceService.GetPresignedResourcesAsync(
            request.UserId,
            resourceIds,
            cancellationToken);

        if (presignResult.IsFailure)
        {
            return Result.Failure<IEnumerable<WorkspaceAiResourceResponse>>(presignResult.Error);
        }

        var resourcesById = presignResult.Value.ToDictionary(item => item.ResourceId, item => item);
        var response = resourcePairs
            .Where(pair => resourcesById.ContainsKey(pair.ResourceId))
            .Select(pair => ToResponse(pair, resourcesById[pair.ResourceId]))
            .Where(item => IsTypeAllowed(item.ResourceType, selectedTypes))
            .OrderByDescending(item => item.ChatCreatedAt)
            .ToList();

        return Result.Success<IEnumerable<WorkspaceAiResourceResponse>>(response);
    }

    private static WorkspaceAiResourceResponse ToResponse(
        ChatResourcePair pair,
        UserResourcePresignResult resource)
    {
        return new WorkspaceAiResourceResponse(
            pair.ChatSessionId,
            pair.ChatId,
            pair.ResourceId,
            resource.PresignedUrl,
            resource.ContentType,
            resource.ResourceType,
            pair.ChatCreatedAt);
    }

    private static bool IsTypeAllowed(string? resourceType, IReadOnlySet<string> selectedTypes)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return false;
        }

        var normalized = resourceType.Trim().ToLowerInvariant();
        return selectedTypes.Any(type => normalized.Contains(type, StringComparison.Ordinal));
    }

    private static HashSet<string> NormalizeTypes(IReadOnlyList<string>? resourceTypes)
    {
        if (resourceTypes is null || resourceTypes.Count == 0)
        {
            return AllowedTypes.ToHashSet(StringComparer.Ordinal);
        }

        var normalized = resourceTypes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => AllowedTypes.Contains(value, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        return normalized.Count == 0 ? AllowedTypes.ToHashSet(StringComparer.Ordinal) : normalized;
    }

    private sealed record ChatResourcePair(
        Guid ChatSessionId,
        Guid ChatId,
        Guid ResourceId,
        DateTime? ChatCreatedAt);
}
