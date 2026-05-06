using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record ActivatePublishingScheduleCommand(
    Guid ScheduleId,
    Guid UserId) : IRequest<Result<bool>>;

public sealed class ActivatePublishingScheduleCommandHandler
    : IRequestHandler<ActivatePublishingScheduleCommand, Result<bool>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IPostRepository _postRepository;
    private readonly PublishingScheduleCommandSupport _support;

    public ActivatePublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        IPostPublicationRepository postPublicationRepository,
        Application.Abstractions.SocialMedias.IUserSocialMediaService userSocialMediaService)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _postRepository = postRepository;
        _support = new PublishingScheduleCommandSupport(
            workspaceRepository,
            postRepository,
            postPublicationRepository,
            userSocialMediaService);
    }

    public async Task<Result<bool>> Handle(
        ActivatePublishingScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(request.ScheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return Result.Failure<bool>(PublishingScheduleErrors.NotFound);
        }

        if (schedule.UserId != request.UserId)
        {
            return Result.Failure<bool>(PublishingScheduleErrors.Unauthorized);
        }

        if (string.Equals(schedule.Mode, PublishingScheduleState.AgenticMode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<bool>(PublishingScheduleErrors.UnsupportedModeForHandler);
        }

        var activeItems = schedule.Items
            .Where(item => !item.DeletedAt.HasValue)
            .Select(item => new Models.PublishingScheduleItemInput(
                item.ItemType,
                item.ItemId,
                item.SortOrder,
                item.ExecutionBehavior))
            .ToList();
        var activeTargets = schedule.Targets
            .Where(target => !target.DeletedAt.HasValue)
            .Select(target => new Models.PublishingScheduleTargetInput(
                target.SocialMediaId,
                target.IsPrimary))
            .ToList();

        var validated = await _support.ValidateAsync(
            request.UserId,
            schedule.WorkspaceId,
            schedule.Name,
            schedule.Mode,
            schedule.ExecuteAtUtc,
            schedule.Timezone,
            schedule.IsPrivate,
            schedule.PlatformPreference,
            schedule.AgentPrompt,
            schedule.MaxContentLength,
            null,
            activeItems,
            activeTargets,
            schedule.Id,
            cancellationToken);

        if (validated.IsFailure)
        {
            return Result.Failure<bool>(validated.Error);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        schedule.Status = PublishingScheduleState.StatusScheduled;
        schedule.ErrorCode = null;
        schedule.ErrorMessage = null;
        schedule.NextRetryAt = null;
        schedule.UpdatedAt = now;

        foreach (var item in schedule.Items.Where(item => !item.DeletedAt.HasValue))
        {
            item.Status = PublishingScheduleState.ItemStatusScheduled;
            item.ErrorMessage = null;
            item.UpdatedAt = now;
        }

        PublishingScheduleCommandSupport.ApplyPostScheduling(
            schedule.Id,
            validated.Value.ExecuteAtUtc,
            validated.Value.Timezone,
            validated.Value.Targets.Select(target => target.SocialMediaId).ToList(),
            validated.Value.IsPrivate,
            validated.Value.Posts);

        foreach (var post in validated.Value.Posts)
        {
            _postRepository.Update(post);
        }

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }

}
