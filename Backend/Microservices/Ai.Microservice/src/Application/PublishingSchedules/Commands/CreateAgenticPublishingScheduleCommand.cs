using Application.Abstractions.Automation;
using Application.PublishingSchedules.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record CreateAgenticPublishingScheduleCommand(
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
    IReadOnlyList<PublishingScheduleTargetInput>? Targets,
    string? DesiredPostType = null) : IRequest<Result<PublishingScheduleResponse>>;

public sealed class CreateAgenticPublishingScheduleCommandHandler
    : IRequestHandler<CreateAgenticPublishingScheduleCommand, Result<PublishingScheduleResponse>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly PublishingScheduleCommandSupport _support;
    private readonly PublishingScheduleResponseBuilder _responseBuilder;

    public CreateAgenticPublishingScheduleCommandHandler(
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
        CreateAgenticPublishingScheduleCommand request,
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
            request.PlatformPreference,
            request.AgentPrompt,
            request.MaxContentLength,
            request.Search,
            null,
            request.Targets,
            null,
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
        var executionContext = new AgenticScheduleExecutionContext(
            Search: new PublishingScheduleSearchInput(
                validated.Value.Search!.QueryTemplate,
                validated.Value.Search.Count,
                validated.Value.Search.Country,
                validated.Value.Search.SearchLanguage,
                validated.Value.Search.Freshness),
            DesiredPostType: NormalizePostType(request.DesiredPostType),
            RegisteredAtUtc: now);

        var schedule = new PublishingSchedule
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            Name = validated.Value.Name,
            Mode = validated.Value.Mode,
            Status = PublishingScheduleState.StatusWaitingForExecution,
            Timezone = validated.Value.Timezone,
            ExecuteAtUtc = validated.Value.ExecuteAtUtc,
            IsPrivate = validated.Value.IsPrivate,
            CreatedBy = PublishingScheduleState.CreatedByUser,
            PlatformPreference = validated.Value.PlatformPreference,
            AgentPrompt = validated.Value.AgentPrompt,
            MaxContentLength = validated.Value.MaxContentLength,
            SearchQueryTemplate = validated.Value.Search!.QueryTemplate,
            ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(executionContext),
            CreatedAt = now,
            UpdatedAt = now,
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

        await _publishingScheduleRepository.AddAsync(schedule, cancellationToken);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var response = await _responseBuilder.BuildAsync(schedule, cancellationToken);
        return Result.Success(response);
    }

    private static string? NormalizePostType(string? postType)
    {
        return (postType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "reel" or "reels" or "video" => "reels",
            "post" or "posts" => "posts",
            _ => null
        };
    }
}
