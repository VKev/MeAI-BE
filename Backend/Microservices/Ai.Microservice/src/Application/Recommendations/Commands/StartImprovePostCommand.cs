using Application.Posts;
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

/// <summary>
/// Async "improve this existing post" start command. Parallel to
/// <see cref="StartDraftPostGenerationCommand"/>, but operates on an existing
/// <see cref="Post"/>. Replace-on-rerun: if the target post already has a
/// <c>RecommendPostId</c>, the existing RecommendPost row is hard-deleted before
/// the new one is inserted — the system holds at most one suggestion per post.
/// </summary>
public sealed record StartImprovePostCommand(
    Guid UserId,
    Guid PostId,
    bool ImproveCaption,
    bool ImproveImage,
    string? Style = null,
    string? UserInstruction = null) : IRequest<Result<RecommendPostTaskResponse>>;

public sealed class StartImprovePostCommandHandler
    : IRequestHandler<StartImprovePostCommand, Result<RecommendPostTaskResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IRecommendPostRepository _recommendPostRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<StartImprovePostCommandHandler> _logger;

    public StartImprovePostCommandHandler(
        IPostRepository postRepository,
        IRecommendPostRepository recommendPostRepository,
        IPublishEndpoint publishEndpoint,
        ILogger<StartImprovePostCommandHandler> logger)
    {
        _postRepository = postRepository;
        _recommendPostRepository = recommendPostRepository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<RecommendPostTaskResponse>> Handle(
        StartImprovePostCommand request,
        CancellationToken cancellationToken)
    {
        // ── Validate flags: at least one of caption / image must be requested ──
        if (!request.ImproveCaption && !request.ImproveImage)
        {
            return Result.Failure<RecommendPostTaskResponse>(
                new Error(
                    "ImprovePost.NothingToImprove",
                    "At least one of improveCaption or improveImage must be true."));
        }

        // ── Load + authorize the original post ───────────────────────────────
        // GetByIdForUpdateAsync — we'll be writing back RecommendPostId on it.
        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<RecommendPostTaskResponse>(PostErrors.NotFound);
        }
        if (post.UserId != request.UserId)
        {
            return Result.Failure<RecommendPostTaskResponse>(PostErrors.Unauthorized);
        }

        // ── Validate style ──────────────────────────────────────────────────
        // Strict: an explicit unknown value is rejected, omitted/null falls back to
        // the original post's stored style if available, else "branded".
        if (!DraftPostStyles.TryValidate(request.Style, out var validatedStyle))
        {
            return Result.Failure<RecommendPostTaskResponse>(
                new Error(
                    "ImprovePost.InvalidStyle",
                    $"style '{request.Style}' is not supported. Allowed values: {string.Join(", ", DraftPostStyles.All)}. Omit to inherit from the original post."));
        }

        // The TryValidate fallback for null is "branded" — but we want to prefer
        // the post's existing style if the caller didn't specify, so override only
        // when the caller actually omitted style.
        var style = string.IsNullOrWhiteSpace(request.Style)
            ? DraftPostStyles.NormalizeOrDefault(post.Content?.PostType)
            : validatedStyle;

        // ── Replace-on-rerun: delete any prior RecommendPost for this post ──
        var existing = await _recommendPostRepository.GetByOriginalPostIdForUpdateAsync(
            request.PostId, cancellationToken);
        if (existing is not null)
        {
            _recommendPostRepository.Remove(existing);
            _logger.LogInformation(
                "ImprovePost: replacing existing RecommendPost {OldId} for PostId={PostId}",
                existing.Id, request.PostId);
        }

        var correlationId = Guid.CreateVersion7();
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var trimmedInstruction = string.IsNullOrWhiteSpace(request.UserInstruction)
            ? null
            : request.UserInstruction.Trim();

        var entity = new RecommendPost
        {
            Id = Guid.CreateVersion7(),
            CorrelationId = correlationId,
            UserId = request.UserId,
            WorkspaceId = post.WorkspaceId,
            OriginalPostId = post.Id,
            ImproveCaption = request.ImproveCaption,
            ImproveImage = request.ImproveImage,
            Style = style,
            UserInstruction = trimmedInstruction,
            Status = RecommendPostStatuses.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _recommendPostRepository.AddAsync(entity, cancellationToken);

        // Set the FK on the post AT submit time — the GET endpoint joins through
        // this so the FE can poll status while the consumer is still working.
        post.RecommendPostId = entity.Id;
        post.UpdatedAt = now;
        _postRepository.Update(post);

        // Single SaveChangesAsync covers Remove(old) + Add(new) + Update(post) so
        // we don't briefly violate the unique index on Post.RecommendPostId.
        await _recommendPostRepository.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(
            new GenerateRecommendPostStarted
            {
                CorrelationId = correlationId,
                UserId = request.UserId,
                WorkspaceId = post.WorkspaceId,
                OriginalPostId = post.Id,
                ImproveCaption = request.ImproveCaption,
                ImproveImage = request.ImproveImage,
                Style = style,
                UserInstruction = trimmedInstruction,
                StartedAt = now,
            },
            cancellationToken);

        _logger.LogInformation(
            "ImprovePost queued. CorrelationId={CorrelationId} UserId={UserId} PostId={PostId} ImproveCaption={Caption} ImproveImage={Image} Style={Style}",
            correlationId,
            request.UserId,
            post.Id,
            request.ImproveCaption,
            request.ImproveImage,
            style);

        return Result.Success(MapToResponse(entity));
    }

    internal static RecommendPostTaskResponse MapToResponse(RecommendPost task)
    {
        return new RecommendPostTaskResponse(
            Id: task.Id,
            CorrelationId: task.CorrelationId,
            Status: task.Status,
            OriginalPostId: task.OriginalPostId,
            UserId: task.UserId,
            WorkspaceId: task.WorkspaceId,
            ImproveCaption: task.ImproveCaption,
            ImproveImage: task.ImproveImage,
            Style: task.Style,
            UserInstruction: task.UserInstruction,
            ResultCaption: task.ResultCaption,
            ResultResourceId: task.ResultResourceId,
            ResultPresignedUrl: task.ResultPresignedUrl,
            ErrorCode: task.ErrorCode,
            ErrorMessage: task.ErrorMessage,
            CreatedAt: task.CreatedAt,
            CompletedAt: task.CompletedAt);
    }
}
