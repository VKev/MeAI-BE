using Application.Abstractions.Automation;
using Application.PublishingSchedules.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record UpdateAgenticPublishingScheduleCommand(
    Guid ScheduleId,
    Guid UserId,
    Guid WorkspaceId,
    string? Name,
    string? Mode,
    DateTime ExecuteAtUtc,
    string? Timezone,
    bool? IsPrivate,
    string? PlatformPreference,
    string? AgentPrompt,
    int? MaxContentLength,
    PublishingScheduleSearchInput? Search,
    IReadOnlyList<PublishingScheduleTargetInput>? Targets) : IRequest<Result<PublishingScheduleResponse>>;

public sealed class UpdateAgenticPublishingScheduleCommandHandler
    : IRequestHandler<UpdateAgenticPublishingScheduleCommand, Result<PublishingScheduleResponse>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly PublishingScheduleCommandSupport _support;
    private readonly PublishingScheduleResponseBuilder _responseBuilder;

    public UpdateAgenticPublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IWorkspaceRepository workspaceRepository,
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        Application.Abstractions.SocialMedias.IUserSocialMediaService userSocialMediaService,
        PublishingScheduleResponseBuilder responseBuilder)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _support = new PublishingScheduleCommandSupport(
            workspaceRepository,
            postRepository,
            postPublicationRepository,
            userSocialMediaService);
        _responseBuilder = responseBuilder;
    }

    public async Task<Result<PublishingScheduleResponse>> Handle(
        UpdateAgenticPublishingScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(request.ScheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.NotFound);
        }

        if (schedule.UserId != request.UserId)
        {
            return Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.Unauthorized);
        }

        var validated = await _support.ValidateAsync(
            request.UserId,
            request.WorkspaceId,
            request.Name,
            request.Mode,
            request.ExecuteAtUtc,
            request.Timezone,
            request.IsPrivate,
            request.PlatformPreference,
            request.AgentPrompt,
            request.MaxContentLength,
            request.Search,
            null,
            request.Targets,
            schedule.Id,
            cancellationToken);

        if (validated.IsFailure)
        {
            return Result.Failure<PublishingScheduleResponse>(validated.Error);
        }

        if (!string.Equals(validated.Value.Mode, PublishingScheduleState.AgenticMode, StringComparison.Ordinal))
        {
            return Result.Failure<PublishingScheduleResponse>(PublishingScheduleErrors.UnsupportedModeForHandler);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var target in schedule.Targets.Where(target => !target.DeletedAt.HasValue))
        {
            target.DeletedAt = now;
            target.UpdatedAt = now;
        }

        foreach (var item in schedule.Items.Where(item => !item.DeletedAt.HasValue))
        {
            item.DeletedAt = now;
            item.UpdatedAt = now;
        }

        foreach (var target in validated.Value.Targets)
        {
            schedule.Targets.Add(new Domain.Entities.PublishingScheduleTarget
            {
                Id = Guid.CreateVersion7(),
                ScheduleId = schedule.Id,
                SocialMediaId = target.SocialMediaId,
                Platform = validated.Value.SocialMediaById[target.SocialMediaId].Type,
                TargetLabel = validated.Value.SocialMediaById[target.SocialMediaId].Type,
                IsPrimary = target.IsPrimary,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var executionContext = new AgenticScheduleExecutionContext(
            Search: new PublishingScheduleSearchInput(
                validated.Value.Search!.QueryTemplate,
                validated.Value.Search.Count,
                validated.Value.Search.Country,
                validated.Value.Search.SearchLanguage,
                validated.Value.Search.Freshness),
            RegisteredAtUtc: now);

        schedule.WorkspaceId = request.WorkspaceId;
        schedule.Name = validated.Value.Name;
        schedule.Mode = validated.Value.Mode;
        schedule.Status = PublishingScheduleState.StatusWaitingForExecution;
        schedule.Timezone = validated.Value.Timezone;
        schedule.ExecuteAtUtc = validated.Value.ExecuteAtUtc;
        schedule.IsPrivate = validated.Value.IsPrivate;
        schedule.PlatformPreference = validated.Value.PlatformPreference;
        schedule.AgentPrompt = validated.Value.AgentPrompt;
        schedule.MaxContentLength = validated.Value.MaxContentLength;
        schedule.SearchQueryTemplate = validated.Value.Search!.QueryTemplate;
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(executionContext);
        schedule.ErrorCode = null;
        schedule.ErrorMessage = null;
        schedule.NextRetryAt = null;
        schedule.UpdatedAt = now;

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var response = await _responseBuilder.BuildAsync(schedule, cancellationToken);
        return Result.Success(response);
    }
}
