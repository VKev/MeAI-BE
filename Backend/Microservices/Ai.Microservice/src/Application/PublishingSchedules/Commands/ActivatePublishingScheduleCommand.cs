using Application.Abstractions.Automation;
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
    private readonly IN8nWorkflowClient _n8nWorkflowClient;

    public ActivatePublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        IPostPublicationRepository postPublicationRepository,
        Application.Abstractions.SocialMedias.IUserSocialMediaService userSocialMediaService,
        IN8nWorkflowClient n8nWorkflowClient)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _postRepository = postRepository;
        _support = new PublishingScheduleCommandSupport(
            workspaceRepository,
            postRepository,
            postPublicationRepository,
            userSocialMediaService);
        _n8nWorkflowClient = n8nWorkflowClient;
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
            return await ActivateAgenticAsync(schedule, cancellationToken);
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

    private async Task<Result<bool>> ActivateAgenticAsync(
        Domain.Entities.PublishingSchedule schedule,
        CancellationToken cancellationToken)
    {
        var executionContext = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        var validated = await _support.ValidateAsync(
            schedule.UserId,
            schedule.WorkspaceId,
            schedule.Name,
            schedule.Mode,
            schedule.ExecuteAtUtc,
            schedule.Timezone,
            schedule.IsPrivate,
            schedule.PlatformPreference,
            schedule.AgentPrompt,
            executionContext.Search,
            null,
            schedule.Targets.Where(target => !target.DeletedAt.HasValue)
                .Select(target => new Models.PublishingScheduleTargetInput(target.SocialMediaId, target.IsPrimary))
                .ToList(),
            schedule.Id,
            cancellationToken);

        if (validated.IsFailure)
        {
            return Result.Failure<bool>(validated.Error);
        }

        var jobId = Guid.CreateVersion7();
        var registerResult = await _n8nWorkflowClient.RegisterScheduledAgentJobAsync(
            new N8nScheduledAgentJobRequest(
                jobId,
                schedule.Id,
                schedule.UserId,
                schedule.WorkspaceId,
                schedule.ExecuteAtUtc,
                schedule.Timezone ?? "UTC",
                new N8nWebSearchRequest(
                    validated.Value.Search!.QueryTemplate,
                    validated.Value.Search.Count,
                    validated.Value.Search.Country,
                    validated.Value.Search.SearchLanguage,
                    validated.Value.Search.Freshness,
                    schedule.Timezone,
                    schedule.ExecuteAtUtc)),
            cancellationToken);

        if (registerResult.IsFailure)
        {
            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = registerResult.Error.Code;
            schedule.ErrorMessage = registerResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(registerResult.Error);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        schedule.Status = PublishingScheduleState.StatusWaitingForExecution;
        schedule.ErrorCode = null;
        schedule.ErrorMessage = null;
        schedule.NextRetryAt = null;
        schedule.LastExecutionAt = null;
        schedule.UpdatedAt = now;
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(
            executionContext with
            {
                Search = executionContext.Search ?? new Models.PublishingScheduleSearchInput(
                    validated.Value.Search!.QueryTemplate,
                    validated.Value.Search.Count,
                    validated.Value.Search.Country,
                    validated.Value.Search.SearchLanguage,
                    validated.Value.Search.Freshness),
                N8nJobId = jobId,
                N8nExecutionId = registerResult.Value.ExecutionId,
                RegisteredAtUtc = registerResult.Value.AcceptedAtUtc,
                LastProcessedCallbackJobId = null
            });

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
