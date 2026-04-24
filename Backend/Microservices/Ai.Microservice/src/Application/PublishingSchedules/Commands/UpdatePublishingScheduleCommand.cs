using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record UpdatePublishingScheduleCommand(
    Guid ScheduleId,
    Guid UserId,
    Guid WorkspaceId,
    string? Name,
    string? Mode,
    DateTime ExecuteAtUtc,
    string? Timezone,
    bool? IsPrivate,
    IReadOnlyList<PublishingScheduleItemInput>? Items,
    IReadOnlyList<PublishingScheduleTargetInput>? Targets) : IRequest<Result<PublishingScheduleResponse>>;

public sealed class UpdatePublishingScheduleCommandHandler
    : IRequestHandler<UpdatePublishingScheduleCommand, Result<PublishingScheduleResponse>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IPostRepository _postRepository;
    private readonly PublishingScheduleCommandSupport _support;
    private readonly PublishingScheduleResponseBuilder _responseBuilder;

    public UpdatePublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        IPostPublicationRepository postPublicationRepository,
        Application.Abstractions.SocialMedias.IUserSocialMediaService userSocialMediaService,
        PublishingScheduleResponseBuilder responseBuilder)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _postRepository = postRepository;
        _support = new PublishingScheduleCommandSupport(
            workspaceRepository,
            postRepository,
            postPublicationRepository,
            userSocialMediaService);
        _responseBuilder = responseBuilder;
    }

    public async Task<Result<PublishingScheduleResponse>> Handle(
        UpdatePublishingScheduleCommand request,
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
            null,
            null,
            null,
            request.Items,
            request.Targets,
            schedule.Id,
            cancellationToken);

        if (validated.IsFailure)
        {
            return Result.Failure<PublishingScheduleResponse>(validated.Error);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var existingPostIds = schedule.Items
            .Where(item => !item.DeletedAt.HasValue && string.Equals(item.ItemType, PublishingScheduleState.ItemTypePost, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ItemId)
            .Distinct()
            .ToList();
        var existingPosts = existingPostIds.Count == 0
            ? []
            : await _postRepository.GetByIdsForUpdateAsync(existingPostIds, cancellationToken);

        PublishingScheduleCommandSupport.ClearPostScheduling(schedule.Id, existingPosts);

        schedule.WorkspaceId = request.WorkspaceId;
        schedule.Name = validated.Value.Name;
        schedule.Mode = validated.Value.Mode;
        schedule.Status = PublishingScheduleState.StatusScheduled;
        schedule.Timezone = validated.Value.Timezone;
        schedule.ExecuteAtUtc = validated.Value.ExecuteAtUtc;
        schedule.IsPrivate = validated.Value.IsPrivate;
        schedule.PlatformPreference = validated.Value.PlatformPreference;
        schedule.AgentPrompt = validated.Value.AgentPrompt;
        schedule.ExecutionContextJson = null;
        schedule.ErrorCode = null;
        schedule.ErrorMessage = null;
        schedule.NextRetryAt = null;
        schedule.UpdatedAt = now;

        foreach (var item in schedule.Items.Where(item => !item.DeletedAt.HasValue))
        {
            item.DeletedAt = now;
            item.UpdatedAt = now;
        }

        foreach (var target in schedule.Targets.Where(target => !target.DeletedAt.HasValue))
        {
            target.DeletedAt = now;
            target.UpdatedAt = now;
        }

        foreach (var item in validated.Value.Items)
        {
            schedule.Items.Add(new PublishingScheduleItem
            {
                Id = Guid.CreateVersion7(),
                ScheduleId = schedule.Id,
                ItemType = item.ItemType,
                ItemId = item.ItemId,
                SortOrder = item.SortOrder,
                ExecutionBehavior = item.ExecutionBehavior,
                Status = PublishingScheduleState.ItemStatusScheduled,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        foreach (var target in validated.Value.Targets)
        {
            schedule.Targets.Add(new PublishingScheduleTarget
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

        PublishingScheduleCommandSupport.ApplyPostScheduling(
            schedule.Id,
            schedule.ExecuteAtUtc,
            schedule.Timezone,
            validated.Value.Targets.Select(target => target.SocialMediaId).ToList(),
            schedule.IsPrivate,
            validated.Value.Posts);

        foreach (var post in existingPosts.Concat(validated.Value.Posts).DistinctBy(post => post.Id))
        {
            _postRepository.Update(post);
        }

        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var response = await _responseBuilder.BuildAsync(schedule, cancellationToken);
        return Result.Success(response);
    }
}
