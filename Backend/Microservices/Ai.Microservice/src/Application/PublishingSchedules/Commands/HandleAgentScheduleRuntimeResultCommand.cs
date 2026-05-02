using Application.Abstractions.Automation;
using Application.Posts.Commands;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record HandleAgentScheduleRuntimeResultCommand(
    Guid ScheduleId,
    Guid JobId,
    N8nWebSearchResponse Search,
    Guid? CorrelationId = null,
    int? AttemptNumber = null) : IRequest<Result<bool>>;

public sealed class HandleAgentScheduleRuntimeResultCommandHandler
    : IRequestHandler<HandleAgentScheduleRuntimeResultCommand, Result<bool>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IAgenticRuntimeContentService _runtimeContentService;
    private readonly IWebSearchEnrichmentService _webSearchEnrichmentService;
    private readonly IMediator _mediator;

    public HandleAgentScheduleRuntimeResultCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IAgenticRuntimeContentService runtimeContentService,
        IWebSearchEnrichmentService webSearchEnrichmentService,
        IMediator mediator)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _runtimeContentService = runtimeContentService;
        _webSearchEnrichmentService = webSearchEnrichmentService;
        _mediator = mediator;
    }

    public async Task<Result<bool>> Handle(
        HandleAgentScheduleRuntimeResultCommand request,
        CancellationToken cancellationToken)
    {
        var schedule = await _publishingScheduleRepository.GetByIdForUpdateAsync(request.ScheduleId, cancellationToken);
        if (schedule is null || schedule.DeletedAt.HasValue)
        {
            return Result.Failure<bool>(PublishingScheduleErrors.NotFound);
        }

        if (!string.Equals(schedule.Mode, PublishingScheduleState.AgenticMode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<bool>(PublishingScheduleErrors.UnsupportedModeForHandler);
        }

        if (string.Equals(schedule.Status, PublishingScheduleState.StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(true);
        }

        var context = AgenticScheduleExecutionContextSerializer.Parse(schedule.ExecutionContextJson);
        if (context.LastProcessedCallbackJobId == request.JobId)
        {
            return Result.Success(true);
        }

        var enrichedSearch = await _webSearchEnrichmentService.EnrichAsync(
            request.Search,
            schedule.UserId,
            schedule.WorkspaceId,
            null,
            null,
            cancellationToken);

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        schedule.Status = PublishingScheduleState.StatusExecuting;
        schedule.LastExecutionAt = now;
        schedule.UpdatedAt = now;
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context with
        {
            LastProcessedCallbackJobId = request.JobId,
            LastCallbackReceivedAtUtc = now,
            LastQuery = enrichedSearch.Query,
            LastRetrievedAtUtc = enrichedSearch.RetrievedAtUtc,
            LastSearchPayload = enrichedSearch
        });
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var contentDraftResult = await _runtimeContentService.GeneratePostDraftAsync(
            new AgenticRuntimeContentRequest(
                schedule.Id,
                schedule.Name,
                schedule.AgentPrompt,
                schedule.PlatformPreference,
                enrichedSearch),
            cancellationToken);

        if (contentDraftResult.IsFailure)
        {
            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = contentDraftResult.Error.Code;
            schedule.ErrorMessage = contentDraftResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(contentDraftResult.Error);
        }

        var importedResourceIds = enrichedSearch.ImportedResources?
            .Select(item => item.ResourceId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => id.ToString())
            .ToList() ?? [];

        var createPostResult = await _mediator.Send(
            new CreatePostCommand(
                schedule.UserId,
                schedule.WorkspaceId,
                null,
                null,
                contentDraftResult.Value.Title,
                new PostContent
                {
                    Content = contentDraftResult.Value.Content,
                    Hashtag = contentDraftResult.Value.Hashtag,
                    PostType = contentDraftResult.Value.PostType,
                    ResourceList = importedResourceIds
                },
                "draft",
                null,
                schedule.PlatformPreference,
                PostBuilderOriginKinds.AiOther),
            cancellationToken);

        if (createPostResult.IsFailure)
        {
            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = createPostResult.Error.Code;
            schedule.ErrorMessage = createPostResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(createPostResult.Error);
        }

        var runtimePostId = createPostResult.Value.Id;
        var runtimeItem = new PublishingScheduleItem
        {
            Id = Guid.CreateVersion7(),
            ScheduleId = schedule.Id,
            ItemType = PublishingScheduleState.ItemTypePost,
            ItemId = runtimePostId,
            SortOrder = schedule.Items.Count(item => !item.DeletedAt.HasValue) + 1,
            ExecutionBehavior = PublishingScheduleState.ExecutionBehaviorPublishAll,
            Status = PublishingScheduleState.ItemStatusPublishing,
            LastExecutionAt = DateTimeExtensions.PostgreSqlUtcNow,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
            UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };
        schedule.Items.Add(runtimeItem);
        schedule.Status = PublishingScheduleState.StatusPublishing;
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context with
        {
            LastProcessedCallbackJobId = request.JobId,
            RuntimePostId = runtimePostId,
            LastCallbackReceivedAtUtc = now,
            LastQuery = enrichedSearch.Query,
            LastRetrievedAtUtc = enrichedSearch.RetrievedAtUtc,
            LastSearchPayload = enrichedSearch
        });
        schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var publishResult = await _mediator.Send(
            new PublishPostsCommand(
                schedule.UserId,
                [
                    new PublishPostTargetInput(
                        runtimePostId,
                        schedule.Targets.Where(target => !target.DeletedAt.HasValue).Select(target => target.SocialMediaId).ToList(),
                        schedule.IsPrivate,
                        schedule.Id)
                ]),
            cancellationToken);

        if (publishResult.IsFailure)
        {
            runtimeItem.Status = PublishingScheduleState.ItemStatusFailed;
            runtimeItem.ErrorMessage = publishResult.Error.Description;
            runtimeItem.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = publishResult.Error.Code;
            schedule.ErrorMessage = publishResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(publishResult.Error);
        }

        return Result.Success(true);
    }
}
