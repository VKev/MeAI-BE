using System.Text.Json;
using Application.Abstractions.Configs;
using Application.Abstractions.Resources;
using Application.ChatSessions;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.ImageGenerating;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.Chats.Commands;

public sealed record CreateChatImageCommand(
    Guid UserId,
    Guid ChatSessionId,
    string Prompt,
    IReadOnlyList<Guid> ResourceIds,
    string? AspectRatio,
    string? Resolution,
    string? OutputFormat) : IRequest<Result<ChatImageResponse>>;

public sealed record ChatImageResponse(
    Guid ChatId,
    Guid CorrelationId);

public sealed class CreateChatImageCommandHandler
    : IRequestHandler<CreateChatImageCommand, Result<ChatImageResponse>>
{
    private readonly IChatRepository _chatRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IUserConfigService _userConfigService;
    private readonly IUserResourceService _userResourceService;
    private readonly IBus _bus;

    public CreateChatImageCommandHandler(
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

    public async Task<Result<ChatImageResponse>> Handle(
        CreateChatImageCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<ChatImageResponse>(new Error("Chat.InvalidPrompt", "Prompt is required."));
        }

        var session = await _chatSessionRepository.GetByIdAsync(request.ChatSessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ChatImageResponse>(ChatSessionErrors.NotFound);
        }

        if (session.UserId != request.UserId)
        {
            return Result.Failure<ChatImageResponse>(ChatSessionErrors.Unauthorized);
        }

        var resourceIds = request.ResourceIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        List<string> imageUrls = new();
        if (resourceIds.Count > 0)
        {
            var presignResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                resourceIds,
                cancellationToken);

            if (presignResult.IsFailure)
            {
                return Result.Failure<ChatImageResponse>(presignResult.Error);
            }

            var presignedById = presignResult.Value.ToDictionary(r => r.ResourceId, r => r.PresignedUrl);
            imageUrls = resourceIds
                .Where(id => presignedById.TryGetValue(id, out _))
                .Select(id => presignedById[id])
                .ToList();
        }

        var activeConfig = await TryGetActiveConfigAsync(cancellationToken);
        var aspectRatio = ResolveAspectRatio(request.AspectRatio, activeConfig?.MediaAspectRatio, "1:1");
        var resolution = string.IsNullOrWhiteSpace(request.Resolution) ? "1K" : request.Resolution.Trim();
        var outputFormat = string.IsNullOrWhiteSpace(request.OutputFormat) ? "png" : request.OutputFormat.Trim();

        var correlationId = Guid.CreateVersion7();

        var config = new ChatImageConfig(
            correlationId,
            aspectRatio,
            resolution,
            outputFormat);

        var chat = new Chat
        {
            Id = Guid.CreateVersion7(),
            SessionId = request.ChatSessionId,
            Prompt = request.Prompt.Trim(),
            Config = JsonSerializer.Serialize(config),
            ReferenceResourceIds = JsonSerializer.Serialize(resourceIds.Select(id => id.ToString())),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _chatRepository.AddAsync(chat, cancellationToken);
        await _chatRepository.SaveChangesAsync(cancellationToken);

        var message = new ImageGenerationStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            Prompt = chat.Prompt ?? string.Empty,
            ImageUrls = imageUrls,
            AspectRatio = aspectRatio,
            Resolution = resolution,
            OutputFormat = outputFormat,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                NotificationTypes.AiImageGenerationSubmitted,
                "Image generation started",
                "Your image request was accepted and is being processed.",
                new
                {
                    correlationId,
                    chatId = chat.Id,
                    resourceIds,
                    aspectRatio,
                    resolution,
                    outputFormat
                },
                request.UserId,
                message.CreatedAt),
            cancellationToken);

        return Result.Success(new ChatImageResponse(chat.Id, correlationId));
    }

    private sealed record ChatImageConfig(
        Guid CorrelationId,
        string AspectRatio,
        string Resolution,
        string OutputFormat);

    private async Task<UserAiConfig?> TryGetActiveConfigAsync(CancellationToken cancellationToken)
    {
        var result = await _userConfigService.GetActiveConfigAsync(cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    private static string ResolveAspectRatio(string? requestedValue, string? configuredValue, string fallback)
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
