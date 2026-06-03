using Application.Abstractions.SocialMedias;
using Application.Recommendations.Models;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Recommendations;

namespace Application.Recommendations.Commands;

public sealed record StartContentSuggestionCommand(
    Guid UserId,
    Guid SocialMediaId,
    ContentSuggestionRequest Request) : IRequest<Result<ContentSuggestionTaskResponse>>;

public sealed class StartContentSuggestionCommandHandler
    : IRequestHandler<StartContentSuggestionCommand, Result<ContentSuggestionTaskResponse>>
{
#pragma warning disable CS0169
    private readonly RecommendPost? _domainDependency;
#pragma warning restore CS0169

    private const int DefaultTopK = 6;
    private const int MaxTopK = 20;
    private const int DefaultMaxRagPosts = 30;
    private const int MaxRagPosts = 200;

    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<StartContentSuggestionCommandHandler> _logger;

    public StartContentSuggestionCommandHandler(
        IUserSocialMediaService userSocialMediaService,
        IPublishEndpoint publishEndpoint,
        ILogger<StartContentSuggestionCommandHandler> logger)
    {
        _userSocialMediaService = userSocialMediaService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<ContentSuggestionTaskResponse>> Handle(
        StartContentSuggestionCommand request,
        CancellationToken cancellationToken)
    {
        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<ContentSuggestionTaskResponse>(socialMediaResult.Error);
        }

        var socialMedia = socialMediaResult.Value.FirstOrDefault();
        if (socialMedia is null)
        {
            return Result.Failure<ContentSuggestionTaskResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        if (!DraftPostStyles.TryValidate(request.Request.Style, out var style))
        {
            return Result.Failure<ContentSuggestionTaskResponse>(
                new Error(
                    "ContentSuggestion.InvalidStyle",
                    $"style '{request.Request.Style}' is not supported. Allowed values: {string.Join(", ", DraftPostStyles.All)}. Omit to use the default 'branded'."));
        }

        if (!DraftPostMediaTypes.TryValidate(request.Request.MediaType, out var mediaType))
        {
            return Result.Failure<ContentSuggestionTaskResponse>(
                new Error(
                    "ContentSuggestion.InvalidMediaType",
                    $"mediaType '{request.Request.MediaType}' is not supported. Allowed values: {string.Join(", ", DraftPostMediaTypes.All)}. Omit to use the default 'image'."));
        }

        var topK = Math.Clamp(request.Request.TopK ?? DefaultTopK, 1, MaxTopK);
        var maxRagPosts = Math.Clamp(request.Request.MaxRagPosts ?? DefaultMaxRagPosts, 1, MaxRagPosts);
        var correlationId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await _publishEndpoint.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                NotificationTypes.AiContentSuggestionProcessing,
                "Content suggestion started",
                "AI is finding a fresh, non-duplicate idea for this account.",
                new
                {
                    correlationId,
                    socialMediaId = request.SocialMediaId,
                    workspaceId = request.Request.WorkspaceId,
                    platform = socialMedia.Type,
                    status = "Processing",
                    style,
                    mediaType,
                    instruction = NormalizeInstruction(request.Request.Instruction),
                    createdAt = now,
                },
                createdAt: now,
                source: NotificationSourceConstants.Creator),
            cancellationToken);

        await _publishEndpoint.Publish(
            new GenerateContentSuggestionStarted
            {
                CorrelationId = correlationId,
                UserId = request.UserId,
                SocialMediaId = request.SocialMediaId,
                WorkspaceId = request.Request.WorkspaceId,
                Style = style,
                MediaType = mediaType,
                Instruction = NormalizeInstruction(request.Request.Instruction),
                TopK = topK,
                MaxRagPosts = maxRagPosts,
                RefreshIndex = request.Request.RefreshIndex,
                StartedAt = now,
            },
            cancellationToken);

        _logger.LogInformation(
            "Content suggestion queued. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId} Style={Style} MediaType={MediaType}",
            correlationId,
            request.UserId,
            request.SocialMediaId,
            style,
            mediaType);

        return Result.Success(new ContentSuggestionTaskResponse(
            CorrelationId: correlationId,
            Status: "Submitted",
            SocialMediaId: request.SocialMediaId,
            UserId: request.UserId,
            WorkspaceId: request.Request.WorkspaceId,
            Style: style,
            MediaType: mediaType,
            Instruction: NormalizeInstruction(request.Request.Instruction),
            CreatedAt: now));
    }

    private static string? NormalizeInstruction(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
