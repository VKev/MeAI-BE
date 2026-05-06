using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;

namespace Application.PublishingSchedules;

public sealed class PublishingScheduleResponseBuilder
{
    private readonly IPostRepository _postRepository;

    public PublishingScheduleResponseBuilder(IPostRepository postRepository)
    {
        _postRepository = postRepository;
    }

    public async Task<PublishingScheduleResponse> BuildAsync(
        PublishingSchedule schedule,
        CancellationToken cancellationToken)
    {
        var executionContext = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        var activeItems = schedule.Items
            .Where(item => !item.DeletedAt.HasValue)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToList();

        var postsById = await GetPostsByIdAsync(activeItems, cancellationToken);

        var responseStatus = DeriveScheduleStatus(schedule, activeItems);

        return new PublishingScheduleResponse(
            schedule.Id,
            schedule.UserId,
            schedule.WorkspaceId,
            schedule.Name,
            schedule.Mode,
            responseStatus,
            schedule.ExecuteAtUtc,
            schedule.Timezone,
            schedule.IsPrivate,
            schedule.CreatedBy,
            schedule.PlatformPreference,
            schedule.AgentPrompt,
            schedule.MaxContentLength,
            executionContext.Search,
            schedule.ExecutionContextJson,
            activeItems.Select(item =>
            {
                postsById.TryGetValue(item.ItemId, out var post);
                return new PublishingScheduleItemResponse(
                    item.Id,
                    item.ItemType,
                    item.ItemId,
                    item.SortOrder,
                    item.ExecutionBehavior,
                    item.Status,
                    item.ErrorMessage,
                    item.LastExecutionAt,
                    post?.Title,
                    post?.Status);
            }).ToList(),
            schedule.Targets
                .Where(target => !target.DeletedAt.HasValue)
                .OrderByDescending(target => target.IsPrimary)
                .ThenBy(target => target.Id)
                .Select(target => new PublishingScheduleTargetResponse(
                    target.Id,
                    target.SocialMediaId,
                    target.Platform,
                    target.TargetLabel,
                    target.IsPrimary))
                .ToList(),
            schedule.LastExecutionAt,
            schedule.NextRetryAt,
            schedule.ErrorCode,
            schedule.ErrorMessage,
            schedule.CreatedAt,
            schedule.UpdatedAt);
    }

    public async Task<IReadOnlyList<PublishingScheduleResponse>> BuildManyAsync(
        IReadOnlyList<PublishingSchedule> schedules,
        CancellationToken cancellationToken)
    {
        var responses = new List<PublishingScheduleResponse>(schedules.Count);
        foreach (var schedule in schedules)
        {
            responses.Add(await BuildAsync(schedule, cancellationToken));
        }

        return responses;
    }

    private async Task<IReadOnlyDictionary<Guid, Post>> GetPostsByIdAsync(
        IReadOnlyList<PublishingScheduleItem> items,
        CancellationToken cancellationToken)
    {
        var postIds = items
            .Where(item => string.Equals(item.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ItemId)
            .Distinct()
            .ToList();

        if (postIds.Count == 0)
        {
            return new Dictionary<Guid, Post>();
        }

        var posts = await _postRepository.GetByIdsAsync(postIds, cancellationToken);
        return posts.ToDictionary(post => post.Id);
    }

    private static string? DeriveScheduleStatus(
        PublishingSchedule schedule,
        IReadOnlyList<PublishingScheduleItem> activeItems)
    {
        if (string.Equals(schedule.Status, PublishingScheduleState.StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            return PublishingScheduleState.StatusCancelled;
        }

        if (activeItems.Count == 0)
        {
            return schedule.Status;
        }

        if (activeItems.Any(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusPublishing, StringComparison.OrdinalIgnoreCase)))
        {
            return PublishingScheduleState.StatusPublishing;
        }

        if (activeItems.All(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusPublished, StringComparison.OrdinalIgnoreCase)))
        {
            return PublishingScheduleState.StatusCompleted;
        }

        if (activeItems.All(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusCancelled, StringComparison.OrdinalIgnoreCase)))
        {
            return PublishingScheduleState.StatusCancelled;
        }

        if (activeItems.Any(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusFailed, StringComparison.OrdinalIgnoreCase)) &&
            !activeItems.Any(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusScheduled, StringComparison.OrdinalIgnoreCase)) &&
            !activeItems.Any(item => string.Equals(item.Status, PublishingScheduleState.ItemStatusPublishing, StringComparison.OrdinalIgnoreCase)))
        {
            return PublishingScheduleState.StatusFailed;
        }

        return schedule.Status;
    }
}
