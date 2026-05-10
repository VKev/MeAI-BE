using Application.Posts.Models;
using Application.PublishingSchedules;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record UpdatePostCommand(
    Guid PostId,
    Guid UserId,
    Guid? WorkspaceId,
    Guid? ChatSessionId,
    Guid? SocialMediaId,
    string? Title,
    Domain.Entities.PostContent? Content,
    string? Status) : IRequest<Result<PostResponse>>;

public sealed class UpdatePostCommandHandler
    : IRequestHandler<UpdatePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public UpdatePostCommandHandler(
        IPostRepository postRepository,
        IPublishingScheduleRepository publishingScheduleRepository,
        IWorkspaceRepository workspaceRepository,
        IChatSessionRepository chatSessionRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _publishingScheduleRepository = publishingScheduleRepository;
        _workspaceRepository = workspaceRepository;
        _chatSessionRepository = chatSessionRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<PostResponse>> Handle(UpdatePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);

        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<PostResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<PostResponse>(PostErrors.Unauthorized);
        }

        // Treat unprovided fields (null in partial payload) as "don't change" rather than
        // clobbering them — callers sending a partial update (e.g. content-only from the
        // post-builder publish flow) must not wipe workspaceId, status, title, etc.
        var requestedWorkspaceId = NormalizeGuid(request.WorkspaceId);
        var requestedChatSessionId = NormalizeGuid(request.ChatSessionId);
        if (requestedChatSessionId.HasValue)
        {
            var chatSession = await _chatSessionRepository.GetByIdAsync(requestedChatSessionId.Value, cancellationToken);
            if (chatSession is null || chatSession.DeletedAt.HasValue)
            {
                return Result.Failure<PostResponse>(PostErrors.ChatSessionNotFound);
            }

            if (chatSession.UserId != request.UserId)
            {
                return Result.Failure<PostResponse>(PostErrors.Unauthorized);
            }

            if (requestedWorkspaceId.HasValue && chatSession.WorkspaceId != requestedWorkspaceId.Value)
            {
                return Result.Failure<PostResponse>(PostErrors.ChatSessionWorkspaceMismatch);
            }

            if (post.WorkspaceId.HasValue && post.WorkspaceId.Value != chatSession.WorkspaceId)
            {
                return Result.Failure<PostResponse>(PostErrors.ChatSessionWorkspaceMismatch);
            }

            post.ChatSessionId = requestedChatSessionId;
            requestedWorkspaceId ??= chatSession.WorkspaceId;
        }

        if (requestedWorkspaceId.HasValue)
        {
            var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
                requestedWorkspaceId.Value,
                request.UserId,
                cancellationToken);

            if (!workspaceExists)
            {
                return Result.Failure<PostResponse>(PostErrors.WorkspaceNotFound);
            }

            post.WorkspaceId = requestedWorkspaceId;
        }

        var requestedSocialMediaId = NormalizeGuid(request.SocialMediaId);
        if (requestedSocialMediaId.HasValue)
        {
            post.SocialMediaId = requestedSocialMediaId;
        }

        var normalizedTitle = NormalizeString(request.Title);
        if (normalizedTitle is not null)
        {
            post.Title = normalizedTitle;
        }

        if (request.Content is not null)
        {
            post.Content = request.Content;
        }

        var normalizedStatus = NormalizeString(request.Status);
        if (normalizedStatus is not null)
        {
            if (IsDraftStatus(normalizedStatus))
            {
                await ClearSchedulingAsync(post, request.UserId, cancellationToken);
            }

            post.Status = normalizedStatus;
        }

        if (post.ScheduleGroupId.HasValue && post.ScheduledAtUtc.HasValue)
        {
            post.Status = "scheduled";
        }

        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        // Consolidate any sibling duplicates that share this post's (PostBuilder, Platform,
        // post_type) bucket. Legacy rows from pre-dedup releases get soft-deleted on the
        // first edit so GetPostBuilder stops surfacing them.
        if (post.PostBuilderId.HasValue)
        {
            var normalizedPlatform = NormalizePlatform(post.Platform);
            var normalizedPostType = NormalizePostType(post.Content?.PostType);
            var siblings = await _postRepository.GetTrackedByPostBuilderIdAsync(
                post.PostBuilderId.Value,
                cancellationToken);

            var now = DateTimeExtensions.PostgreSqlUtcNow;
            foreach (var sibling in siblings)
            {
                if (sibling.Id == post.Id) continue;
                if (sibling.UserId != post.UserId) continue;
                if (NormalizePlatform(sibling.Platform) != normalizedPlatform) continue;
                if (NormalizePostType(sibling.Content?.PostType) != normalizedPostType) continue;

                sibling.DeletedAt = now;
                sibling.UpdatedAt = now;
            }
        }

        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        var response = await _postResponseBuilder.BuildAsync(request.UserId, post, cancellationToken);
        return Result.Success(response);
    }

    private async Task ClearSchedulingAsync(Domain.Entities.Post post, Guid userId, CancellationToken cancellationToken)
    {
        var scheduleId = post.ScheduleGroupId;
        if (!scheduleId.HasValue && !post.ScheduledAtUtc.HasValue)
        {
            return;
        }

        post.ScheduleGroupId = null;
        post.ScheduledAtUtc = null;
        post.ScheduleTimezone = null;
        post.ScheduledSocialMediaIds = Array.Empty<Guid>();
        post.ScheduledIsPrivate = null;

        if (!scheduleId.HasValue)
        {
            return;
        }

        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(scheduleId.Value, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue || schedule.UserId != userId)
        {
            return;
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var scheduleItem = schedule.Items.FirstOrDefault(item =>
            !item.DeletedAt.HasValue &&
            item.ItemId == post.Id &&
            string.Equals(item.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase));

        if (scheduleItem is null)
        {
            return;
        }

        scheduleItem.Status = PublishingScheduleState.ItemStatusCancelled;
        scheduleItem.ErrorMessage = null;
        scheduleItem.UpdatedAt = now;

        var activePostItems = schedule.Items
            .Where(item => !item.DeletedAt.HasValue &&
                           string.Equals(item.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (activePostItems.All(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusCancelled, StringComparison.OrdinalIgnoreCase)))
        {
            schedule.Status = PublishingScheduleState.StatusCancelled;
        }

        schedule.ErrorCode = null;
        schedule.ErrorMessage = null;
        schedule.NextRetryAt = null;
        schedule.UpdatedAt = now;

        _publishingScheduleRepository.Update(schedule);
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsDraftStatus(string? value)
    {
        return string.Equals(value, "draft", StringComparison.OrdinalIgnoreCase);
    }

    private static Guid? NormalizeGuid(Guid? value)
    {
        return value == Guid.Empty ? null : value;
    }

    private static string NormalizePlatform(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "thread" => "threads",
            "ig" => "instagram",
            "fb" => "facebook",
            _ => v
        };
    }

    private static string NormalizePostType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v == "reel" || v == "reels" || v == "video") return "reels";
        return "posts";
    }
}
