using Application.Posts.Commands;
using Application.PublishingSchedules;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Services;

public sealed class ScheduledPostDispatchService
{
    private const int BatchSize = 20;

    private readonly IPostRepository _postRepository;
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<ScheduledPostDispatchService> _logger;

    public ScheduledPostDispatchService(
        IPostRepository postRepository,
        IPublishingScheduleRepository publishingScheduleRepository,
        IMediator mediator,
        ILogger<ScheduledPostDispatchService> logger)
    {
        _postRepository = postRepository;
        _publishingScheduleRepository = publishingScheduleRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<int> DispatchDuePostsAsync(CancellationToken cancellationToken)
    {
        var claimedPosts = await _postRepository.ClaimDueScheduledPostsAsync(
            DateTime.UtcNow,
            BatchSize,
            cancellationToken);

        foreach (var scheduledPost in claimedPosts)
        {
            if (scheduledPost.PublishingScheduleId.HasValue)
            {
                await MarkScheduleItemPublishingAsync(
                    scheduledPost.PublishingScheduleId.Value,
                    scheduledPost.PostId,
                    cancellationToken);
            }

            var result = await _mediator.Send(
                new PublishPostsCommand(
                    scheduledPost.UserId,
                    [new PublishPostTargetInput(
                        scheduledPost.PostId,
                        scheduledPost.SocialMediaIds,
                        scheduledPost.IsPrivate,
                        scheduledPost.PublishingScheduleId)]),
                cancellationToken);

            if (result.IsFailure)
            {
                await _postRepository.MarkScheduledDispatchFailedAsync(scheduledPost.PostId, cancellationToken);
                if (scheduledPost.PublishingScheduleId.HasValue)
                {
                    await MarkScheduleItemFailedAsync(
                        scheduledPost.PublishingScheduleId.Value,
                        scheduledPost.PostId,
                        result.Error.Description,
                        cancellationToken);
                }

                _logger.LogWarning(
                    "Scheduled publish dispatch failed. PostId: {PostId}, UserId: {UserId}, Code: {Code}, Description: {Description}",
                    scheduledPost.PostId,
                    scheduledPost.UserId,
                    result.Error.Code,
                    result.Error.Description);
            }
        }

        return claimedPosts.Count;
    }

    private async Task MarkScheduleItemPublishingAsync(
        Guid scheduleId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(scheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return;
        }

        schedule.Status = PublishingScheduleState.StatusPublishing;
        schedule.LastExecutionAt = DateTime.UtcNow;
        schedule.UpdatedAt = DateTime.UtcNow;

        var item = schedule.Items.FirstOrDefault(existing =>
            !existing.DeletedAt.HasValue &&
            existing.ItemId == postId &&
            string.Equals(existing.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            item.Status = PublishingScheduleState.ItemStatusPublishing;
            item.ErrorMessage = null;
            item.LastExecutionAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
        }

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkScheduleItemFailedAsync(
        Guid scheduleId,
        Guid postId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(scheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return;
        }

        schedule.Status = PublishingScheduleState.StatusFailed;
        schedule.ErrorCode = "PublishingSchedule.DispatchFailed";
        schedule.ErrorMessage = errorMessage;
        schedule.LastExecutionAt = DateTime.UtcNow;
        schedule.UpdatedAt = DateTime.UtcNow;

        var item = schedule.Items.FirstOrDefault(existing =>
            !existing.DeletedAt.HasValue &&
            existing.ItemId == postId &&
            string.Equals(existing.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            item.Status = PublishingScheduleState.ItemStatusFailed;
            item.ErrorMessage = errorMessage;
            item.LastExecutionAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
        }

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
    }
}
