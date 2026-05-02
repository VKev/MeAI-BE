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
    string UserPrompt,
    Guid? WorkspaceId = null,
    int? TopK = null,
    int? MaxReferenceImages = null,
    int? MaxRagPosts = null) : IRequest<Result<DraftPostTaskResponse>>;

public sealed class StartDraftPostGenerationCommandHandler
    : IRequestHandler<StartDraftPostGenerationCommand, Result<DraftPostTaskResponse>>
{
    private const int DefaultTopK = 6;
    private const int DefaultMaxReferenceImages = 3;
    private const int DefaultMaxRagPosts = 30;
    private const int MaxAllowedTopK = 20;
    private const int MaxAllowedReferenceImages = 4;
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

    public async Task<Result<DraftPostTaskResponse>> Handle(
        StartDraftPostGenerationCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            return Result.Failure<DraftPostTaskResponse>(
                new Error("DraftPost.EmptyPrompt", "userPrompt is required."));
        }

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

        var correlationId = Guid.CreateVersion7();
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var task = new DraftPostTask
        {
            Id = Guid.CreateVersion7(),
            CorrelationId = correlationId,
            UserId = request.UserId,
            SocialMediaId = request.SocialMediaId,
            WorkspaceId = request.WorkspaceId,
            UserPrompt = request.UserPrompt.Trim(),
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
                TopK = topK,
                MaxReferenceImages = maxRefs,
                MaxRagPosts = maxRagPosts,
                StartedAt = now,
            },
            cancellationToken);

        _logger.LogInformation(
            "Draft-post generation queued. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
            correlationId,
            request.UserId,
            request.SocialMediaId);

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
