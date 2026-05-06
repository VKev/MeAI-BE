using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record CreatePublishingScheduleCommand(
    Guid UserId,
    Guid WorkspaceId,
    string? Name,
    string? Mode,
    DateTime ExecuteAtUtc,
    string? Timezone,
    bool? IsPrivate,
    IReadOnlyList<PublishingScheduleItemInput>? Items,
    IReadOnlyList<PublishingScheduleTargetInput>? Targets) : IRequest<Result<PublishingScheduleResponse>>;

public sealed class CreatePublishingScheduleCommandHandler
    : IRequestHandler<CreatePublishingScheduleCommand, Result<PublishingScheduleResponse>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IPostRepository _postRepository;
    private readonly PublishingScheduleCommandSupport _support;
    private readonly PublishingScheduleResponseBuilder _responseBuilder;

    public CreatePublishingScheduleCommandHandler(
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
        CreatePublishingScheduleCommand request,
        CancellationToken cancellationToken)
    {
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
            null,
            request.Items,
            request.Targets,
            null,
            cancellationToken);

        if (validated.IsFailure)
        {
            return Result.Failure<PublishingScheduleResponse>(validated.Error);
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var schedule = new PublishingSchedule
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            Name = validated.Value.Name,
            Mode = validated.Value.Mode,
            Status = PublishingScheduleState.StatusScheduled,
            Timezone = validated.Value.Timezone,
            ExecuteAtUtc = validated.Value.ExecuteAtUtc,
            IsPrivate = validated.Value.IsPrivate,
            CreatedBy = PublishingScheduleState.CreatedByUser,
            PlatformPreference = validated.Value.PlatformPreference,
            AgentPrompt = validated.Value.AgentPrompt,
            ExecutionContextJson = null,
            CreatedAt = now,
            UpdatedAt = now,
            Items = validated.Value.Items.Select(item => new PublishingScheduleItem
            {
                Id = Guid.CreateVersion7(),
                ItemType = item.ItemType,
                ItemId = item.ItemId,
                SortOrder = item.SortOrder,
                ExecutionBehavior = item.ExecutionBehavior,
                Status = PublishingScheduleState.ItemStatusScheduled,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList(),
            Targets = validated.Value.Targets.Select(target => new PublishingScheduleTarget
            {
                Id = Guid.CreateVersion7(),
                SocialMediaId = target.SocialMediaId,
                Platform = validated.Value.SocialMediaById[target.SocialMediaId].Type,
                TargetLabel = validated.Value.SocialMediaById[target.SocialMediaId].Type,
                IsPrimary = target.IsPrimary,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList()
        };

        PublishingScheduleCommandSupport.ApplyPostScheduling(
            schedule.Id,
            schedule.ExecuteAtUtc,
            schedule.Timezone,
            schedule.Targets.Select(target => target.SocialMediaId).ToList(),
            schedule.IsPrivate,
            validated.Value.Posts);

        await _publishingScheduleRepository.AddAsync(schedule, cancellationToken);
        foreach (var post in validated.Value.Posts)
        {
            _postRepository.Update(post);
        }

        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var response = await _responseBuilder.BuildAsync(schedule, cancellationToken);
        return Result.Success(response);
    }
}
