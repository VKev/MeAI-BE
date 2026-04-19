using System.Text.Json;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using Application.ChatSessions;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record CreateChatVideoCommand(
    Guid UserId,
    Guid ChatSessionId,
    string Prompt,
    IReadOnlyList<Guid> ResourceIds,
    string? Model,
    string? AspectRatio,
    int? Seeds,
    bool? EnableTranslation,
    string? Watermark) : IRequest<Result<ChatVideoResponse>>;

public sealed record ChatVideoResponse(
    Guid ChatId,
    Guid CorrelationId);

public sealed class CreateChatVideoCommandHandler
    : IRequestHandler<CreateChatVideoCommand, Result<ChatVideoResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly IBus _bus;

    public CreateChatVideoCommandHandler(
        IChatRepository chatRepository,
        IChatSessionRepository chatSessionRepository,
        IUserConfigService userConfigService,
        IUserResourceService userResourceService,
        IBus bus)
    {
        _chatRepository = chatRepository;
        _chatSessionRepository = chatSessionRepository;
        _userConfigService = userConfigService;
        _userResourceService = userResourceService;
        _bus = bus;
    }

    public async Task<Result<ChatVideoResponse>> Handle(
        CreateChatVideoCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<ChatVideoResponse>(new Error("Chat.InvalidPrompt", "Prompt is required."));
        }

        var session = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ChatVideoResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatVideoResponse>(ChatSessionErrors.Unauthorized);
        }

        var resourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        var imageUrls = new List<string>();
        if (resourceIds.Count > 0)
        {
            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                resourceIds,
                cancellationToken);

            if (presignResult.IsFailure)
            {
                return Result.Failure<ChatVideoResponse>(presignResult.Error);
            }

            var presignedById = presignResult.Value.ToDictionary(r => r.ResourceId, r => r.PresignedUrl);
            imageUrls = resourceIds
                .Where(id => presignedById.TryGetValue(id, out _))
                .Select(id => presignedById[id])
                .ToList();
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var model = ResolveValue(request.Model, activeConfig?.ChatModel, "veo3_fast");
        var aspectRatio = ResolveValue(request.AspectRatio, activeConfig?.MediaAspectRatio, "16:9");
        var enableTranslation = request.EnableTranslation ?? true;

        var correlationId = Guid.CreateVersion7();

        var config = new ChatVideoConfig(
            correlationId,
            model,
            aspectRatio,
            request.Seeds,
            enableTranslation,
            request.Watermark);

        var chat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = request.ChatSessionId,
            Prompt = request.Prompt.Trim(),
            Config = JsonSerializer.Serialize(config),
            ReferenceResourceIds = resourceIds.Count == 0 ? null : JsonSerializer.Serialize(resourceIds.Select(id => id.ToString())),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatRepository.AddAsync(chat, cancellationToken);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        var message = new VideoGenerationStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            Prompt = chat.Prompt ?? string.Empty,
            ImageUrls = imageUrls,
            Model = model,
            AspectRatio = aspectRatio,
            Seeds = request.Seeds,
            EnableTranslation = enableTranslation,
            Watermark = request.Watermark,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                NotificationTypes.AiVideoGenerationSubmitted,
                "Video generation started",
                "Your video request was accepted and is being processed.",
                new
                {
                    correlationId,
                    chatId = chat.Id,
                    resourceIds,
                    model,
                    aspectRatio,
                    request.Seeds,
                    enableTranslation,
                    request.Watermark
                },
                request.UserId,
                message.CreatedAt,
                NotificationSourceConstants.Creator),
            cancellationToken);

        return Result.Success(new ChatVideoResponse(chat.Id, correlationId));
    }

    private sealed record ChatVideoConfig(
        Guid CorrelationId,
        string Model,
        string AspectRatio,
        int? Seeds,
        bool EnableTranslation,
        string? Watermark);

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private static string ResolveValue(string? requestedValue, string? configuredValue, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
        {
            return requestedValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue.Trim();
        }

        return fallback;
    }
}
