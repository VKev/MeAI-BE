using Application.Abstractions.SocialMedias;
using Application.Recommendations.Models;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Recommendations;
using SharedLibrary.Extensions;

namespace Application.Recommendations.Commands;

public sealed record StartDraftPostGenerationCommand(
    Guid UserId,
    Guid SocialMediaId,
    string? UserPrompt = null,
    string? Style = null,
    Guid? WorkspaceId = null,
    int? TopK = null,
    int? MaxReferenceImages = null,
    int? MaxRagPosts = null) : IRequest<Result<DraftPostTaskResponse>>;

public sealed class StartDraftPostGenerationCommandHandler
    : IRequestHandler<StartDraftPostGenerationCommand, Result<DraftPostTaskResponse>>
{
    private const int DefaultTopK = 6;
    private const int DefaultMaxReferenceImages = 4;
    private const int DefaultMaxRagPosts = 30;
    private const int MaxAllowedTopK = 20;
    // Bumped 4 → 8 since the consumer now reranks a broader candidate pool down to
    // the requested cap; image-gen models tolerate up to ~8 refs before quality drops.
    private const int MaxAllowedReferenceImages = 8;
    private const int MaxAllowedRagPosts = 200;

    private readonly IDraftPostTaskRepository _repository;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<StartDraftPostGenerationCommandHandler> _logger;

    public StartDraftPostGenerationCommandHandler(
        IDraftPostTaskRepository repository,
        IUserSocialMediaService userSocialMediaService,
        IPublishEndpoint publishEndpoint,
        ILogger<StartDraftPostGenerationCommandHandler> logger)
    {
        _repository = repository;
        _userSocialMediaService = userSocialMediaService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    /// <summary>
    /// Sentinel stored in <c>task.UserPrompt</c> when the user did not provide a topic.
    /// The actual auto-discovery instruction lives in
    /// <see cref="Infrastructure.Logic.Consumers.DraftPostGenerationConsumer"/>; this
    /// is just a human-readable marker so listings of past drafts make sense.
    /// </summary>
    public const string AutoTopicPlaceholder = "[auto-discovered topic]";

    public async Task<Result<DraftPostTaskResponse>> Handle(
        StartDraftPostGenerationCommand request,
        CancellationToken cancellationToken)
    {
        // userPrompt is now optional. If empty / null / whitespace, we flip to
        // auto-discovery mode: the consumer will instruct the recommendation LLM
        // to pick a topic via page-content RAG + web search.
        var trimmedPrompt = (request.UserPrompt ?? string.Empty).Trim();
        var isAutoTopic = trimmedPrompt.Length == 0;
        var promptForStorage = isAutoTopic ? AutoTopicPlaceholder : trimmedPrompt;

        // Verify the social media account belongs to the user — guards against cross-account access.
        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            new[] { request.SocialMediaId },
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<DraftPostTaskResponse>(socialMediaResult.Error);
        }

        var socialMedia = socialMediaResult.Value.FirstOrDefault();
        if (socialMedia is null)
        {
            return Result.Failure<DraftPostTaskResponse>(
                new Error("SocialMedia.NotFound", "Social media account not found."));
        }

        var topK = Math.Clamp(request.TopK ?? DefaultTopK, 1, MaxAllowedTopK);
        var maxRefs = Math.Clamp(request.MaxReferenceImages ?? DefaultMaxReferenceImages, 1, MaxAllowedReferenceImages);
        var maxRagPosts = Math.Clamp(request.MaxRagPosts ?? DefaultMaxRagPosts, 1, MaxAllowedRagPosts);

        // Validate style strictly: null/empty → "branded" default; anything else must
        // match one of the allowed values (creative / branded / marketing) or we reject.
        if (!DraftPostStyles.TryValidate(request.Style, out var style))
        {
            return Result.Failure<DraftPostTaskResponse>(
                new Error(
                    "DraftPost.InvalidStyle",
                    $"style '{request.Style}' is not supported. Allowed values: {string.Join(", ", DraftPostStyles.All)}. Omit to use the default 'branded'."));
        }

        var correlationId = Guid.CreateVersion7();
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var task = new DraftPostTask
        {
            Id = Guid.CreateVersion7(),
            CorrelationId = correlationId,
            UserId = request.UserId,
            SocialMediaId = request.SocialMediaId,
            WorkspaceId = request.WorkspaceId,
            UserPrompt = promptForStorage,
            IsAutoTopic = isAutoTopic,
            Style = style,
            TopK = topK,
            MaxReferenceImages = maxRefs,
            MaxRagPosts = maxRagPosts,
            Status = DraftPostTaskStatuses.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _repository.AddAsync(task, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(
            new GenerateDraftPostStarted
            {
                CorrelationId = correlationId,
                UserId = request.UserId,
                SocialMediaId = request.SocialMediaId,
                WorkspaceId = request.WorkspaceId,
                UserPrompt = task.UserPrompt,
                IsAutoTopic = isAutoTopic,
                Style = style,
                TopK = topK,
                MaxReferenceImages = maxRefs,
                MaxRagPosts = maxRagPosts,
                StartedAt = now,
            },
            cancellationToken);

        _logger.LogInformation(
            "Draft-post generation queued. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId} Style={Style} AutoTopic={Auto}",
            correlationId,
            request.UserId,
            request.SocialMediaId,
            style,
            isAutoTopic);

        return Result.Success(MapToResponse(task));
    }

    internal static DraftPostTaskResponse MapToResponse(DraftPostTask task)
    {
        return new DraftPostTaskResponse(
            CorrelationId: task.CorrelationId,
            Status: task.Status,
            SocialMediaId: task.SocialMediaId,
            UserId: task.UserId,
            WorkspaceId: task.WorkspaceId,
            UserPrompt: task.UserPrompt,
            IsAutoTopic: task.IsAutoTopic,
            Style: task.Style,
            ResultPostBuilderId: task.ResultPostBuilderId,
            ResultPostId: task.ResultPostId,
            ResultResourceId: task.ResultResourceId,
            ResultPresignedUrl: task.ResultPresignedUrl,
            ResultCaption: task.ResultCaption,
            ErrorCode: task.ErrorCode,
            ErrorMessage: task.ErrorMessage,
            CreatedAt: task.CreatedAt,
            CompletedAt: task.CompletedAt);
    }
}
