using Application.Abstractions.Automation;
using Application.Abstractions.Rag;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.PublishingSchedules;
using Application.PublishingSchedules.Models;
using Application.Recommendations.Commands;
using Application.Recommendations.Queries;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules.Commands;

public sealed record ExecuteAgenticPublishingScheduleCommand(
    Guid ScheduleId) : IRequest<Result<bool>>;

public sealed class ExecuteAgenticPublishingScheduleCommandHandler
    : IRequestHandler<ExecuteAgenticPublishingScheduleCommand, Result<bool>>
{
    private readonly IPublishingScheduleRepository _publishingScheduleRepository;
    private readonly IAgenticRuntimeContentService _runtimeContentService;
    private readonly IAgentWebSearchService _agentWebSearchService;
    private readonly IMediator _mediator;
    private readonly IRagClient _ragClient;
    private readonly IPublishEndpoint _publishEndpoint;

    public ExecuteAgenticPublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IAgenticRuntimeContentService runtimeContentService,
        IAgentWebSearchService agentWebSearchService,
        IMediator mediator,
        IRagClient ragClient,
        IPublishEndpoint publishEndpoint)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _runtimeContentService = runtimeContentService;
        _agentWebSearchService = agentWebSearchService;
        _mediator = mediator;
        _ragClient = ragClient;
        _publishEndpoint = publishEndpoint;
    }

    private async Task<AgenticScheduleExecutionContext> UpdateProgressAsync(
        PublishingSchedule schedule,
        AgenticScheduleExecutionContext context,
        string step,
        string status,
        string message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var logs = new List<AgenticExecutionProgressLog>(context.Steps ?? Array.Empty<AgenticExecutionProgressLog>());

        var existingIndex = logs.FindIndex(l => string.Equals(l.Step, step, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            logs[existingIndex] = new AgenticExecutionProgressLog(step, status, message, now);
        }
        else
        {
            logs.Add(new AgenticExecutionProgressLog(step, status, message, now));
        }

        var updatedContext = context with
        {
            CurrentStep = step,
            CurrentStepStatus = status,
            CurrentStepMessage = message,
            Steps = logs
        };

        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(updatedContext);
        schedule.UpdatedAt = now;
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        try
        {
            var notificationType = status switch
            {
                "Failed" => NotificationTypes.AiPublishingScheduleFailed,
                "Completed" when step == "publishing" => NotificationTypes.AiPublishingScheduleCompleted,
                _ => NotificationTypes.AiPublishingScheduleThinking
            };

            var title = status switch
            {
                "Failed" => "AI scheduling execution failed",
                "Completed" when step == "publishing" => "AI post published successfully",
                _ => "AI posting in progress"
            };

            await _publishEndpoint.Publish(
                NotificationRequestedEventFactory.CreateForUser(
                    schedule.UserId,
                    notificationType,
                    title,
                    message,
                    new
                    {
                        scheduleId = schedule.Id,
                        workspaceId = schedule.WorkspaceId,
                        userId = schedule.UserId,
                        status = schedule.Status,
                        currentStep = step,
                        currentStepStatus = status,
                        currentStepMessage = message,
                        steps = logs,
                        createdAt = now
                    },
                    createdAt: now,
                    source: NotificationSourceConstants.Creator),
                cancellationToken);
        }
        catch
        {
            // Do not fail schedule execution if real-time push notification fails
        }

        return updatedContext;
    }

    public async Task<Result<bool>> Handle(
        ExecuteAgenticPublishingScheduleCommand request,
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
        var search = context.Search ?? (!string.IsNullOrWhiteSpace(schedule.SearchQueryTemplate)
            ? new PublishingScheduleSearchInput(
                schedule.SearchQueryTemplate,
                5,
                null,
                null,
                null)
            : null);

        context = await UpdateProgressAsync(
            schedule,
            context,
            "web_search",
            "Running",
            "Parsing schedule configuration and initiating live web search...",
            cancellationToken);

        if (search is null)
        {
            await UpdateProgressAsync(
                schedule,
                context,
                "web_search",
                "Failed",
                "Search configuration is required but missing from the schedule.",
                cancellationToken);

            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = PublishingScheduleErrors.SearchConfigRequired.Code;
            schedule.ErrorMessage = PublishingScheduleErrors.SearchConfigRequired.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(PublishingScheduleErrors.SearchConfigRequired);
        }

        if (string.IsNullOrWhiteSpace(search.QueryTemplate))
        {
            await UpdateProgressAsync(
                schedule,
                context,
                "web_search",
                "Failed",
                "Search query template is empty or invalid.",
                cancellationToken);

            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = PublishingScheduleErrors.SearchQueryTemplateRequired.Code;
            schedule.ErrorMessage = PublishingScheduleErrors.SearchQueryTemplateRequired.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(PublishingScheduleErrors.SearchQueryTemplateRequired);
        }

        var searchCount = Math.Clamp(search.Count ?? 5, 1, 10);
        var searchResult = await _agentWebSearchService.SearchAsync(
            new AgentWebSearchRequest(
                search.QueryTemplate,
                searchCount,
                search.Country,
                search.SearchLanguage,
                search.Freshness,
                schedule.UserId,
                schedule.WorkspaceId),
            cancellationToken);

        if (searchResult.IsFailure)
        {
            await UpdateProgressAsync(
                schedule,
                context,
                "web_search",
                "Failed",
                $"Web search failed: {searchResult.Error.Description}",
                cancellationToken);

            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = searchResult.Error.Code;
            schedule.ErrorMessage = searchResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(searchResult.Error);
        }

        var enrichedSearch = searchResult.Value;
        context = await UpdateProgressAsync(
            schedule,
            context,
            "web_search",
            "Completed",
            $"Web search completed. Retrieved {enrichedSearch.Results.Count} topic results for query: '{enrichedSearch.Query}'",
            cancellationToken);

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var executionRunId = Guid.CreateVersion7();
        schedule.Status = PublishingScheduleState.StatusExecuting;
        schedule.LastExecutionAt = now;
        schedule.UpdatedAt = now;
        var groundingTarget = ResolveGroundingTarget(schedule);
        var recommendationQuery = BuildRecommendationQuery(schedule, enrichedSearch);
        string? recommendationSummary = null;
        string? recommendationPageProfile = null;
        IReadOnlyList<WebSource>? recommendationWebSources = null;
        string? ragFallbackReason = null;

        context = context with
        {
            LastExecutionRunId = executionRunId,
            LastExecutionStartedAtUtc = now,
            LastQuery = enrichedSearch.Query,
            LastRetrievedAtUtc = enrichedSearch.RetrievedAtUtc,
            GroundingSocialMediaId = groundingTarget?.SocialMediaId,
            LastRecommendationQuery = recommendationQuery,
            LastSearchPayload = enrichedSearch
        };
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context);
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        if (groundingTarget is null)
        {
            await UpdateProgressAsync(
                schedule,
                context,
                "rag_ready",
                "Failed",
                "No active target available to ground the agentic schedule with RAG.",
                cancellationToken);

            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = PublishingScheduleErrors.MissingTargets.Code;
            schedule.ErrorMessage = "No active target available to ground the agentic schedule with RAG.";
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(PublishingScheduleErrors.MissingTargets);
        }

        try
        {
            context = await UpdateProgressAsync(
                schedule,
                context,
                "rag_ready",
                "Running",
                "Waiting for RAG service to be active and online...",
                cancellationToken);

            await _ragClient.WaitForRagReadyAsync(cancellationToken);

            context = await UpdateProgressAsync(
                schedule,
                context,
                "rag_ready",
                "Completed",
                "RAG sidecar microservice is ready.",
                cancellationToken);

            context = await UpdateProgressAsync(
                schedule,
                context,
                "indexing_grounding",
                "Running",
                $"Retrieving and indexing past posts from account '{groundingTarget.SocialMediaId}' in RAG to customize agentic brand voice...",
                cancellationToken);

            var indexResult = await _mediator.Send(
                new IndexSocialAccountPostsCommand(
                    schedule.UserId,
                    groundingTarget.SocialMediaId,
                    30),
                cancellationToken);

            if (indexResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Indexing failed: {indexResult.Error.Code} {indexResult.Error.Description}");
            }

            context = await UpdateProgressAsync(
                schedule,
                context,
                "indexing_grounding",
                "Completed",
                "Successfully indexed recent social media posts in RAG vector database.",
                cancellationToken);

            context = await UpdateProgressAsync(
                schedule,
                context,
                "recommendation_generation",
                "Running",
                "Retrieving personalized topic recommendations grounded in account voice and live web topics...",
                cancellationToken);

            var recommendationResult = await _mediator.Send(
                new QueryAccountRecommendationsQuery(
                    schedule.UserId,
                    groundingTarget.SocialMediaId,
                    recommendationQuery,
                    6),
                cancellationToken);

            if (recommendationResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Recommendation failed: {recommendationResult.Error.Code} {recommendationResult.Error.Description}");
            }

            recommendationSummary = recommendationResult.Value.Answer;
            recommendationPageProfile = recommendationResult.Value.PageProfileText;
            recommendationWebSources = recommendationResult.Value.WebSources;

            context = await UpdateProgressAsync(
                schedule,
                context,
                "recommendation_generation",
                "Completed",
                "Voice-grounded personalized recommendations generated successfully.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            ragFallbackReason = ex.Message;
            var currentFailedStep = context.CurrentStep ?? "indexing_grounding";
            context = await UpdateProgressAsync(
                schedule,
                context,
                currentFailedStep,
                "Failed",
                $"RAG pipeline failed with error: {ex.Message}. Falling back to standard generation.",
                cancellationToken);
        }

        context = context with
        {
            LastExecutionRunId = executionRunId,
            LastExecutionStartedAtUtc = now,
            LastQuery = enrichedSearch.Query,
            LastRetrievedAtUtc = enrichedSearch.RetrievedAtUtc,
            GroundingSocialMediaId = groundingTarget.SocialMediaId,
            LastRecommendationQuery = recommendationQuery,
            LastRecommendationSummary = Truncate(recommendationSummary, 2000),
            LastRagFallbackReason = ragFallbackReason,
            LastSearchPayload = enrichedSearch
        };
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context);
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var targetGroups = GroupActiveTargetsByPlatform(schedule);
        if (targetGroups.Count == 0)
        {
            await UpdateProgressAsync(
                schedule,
                context,
                "draft_generation",
                "Failed",
                "No active target social accounts available for post generation.",
                cancellationToken);

            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = PublishingScheduleErrors.MissingTargets.Code;
            schedule.ErrorMessage = PublishingScheduleErrors.MissingTargets.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(PublishingScheduleErrors.MissingTargets);
        }

        var createdPosts = new List<(PostResponse Post, RuntimeTargetGroup Group)>(targetGroups.Count);
        Guid? postBuilderId = null;

        foreach (var group in targetGroups)
        {
            var stepDraftKey = $"draft_generation_{group.Platform}";
            context = await UpdateProgressAsync(
                schedule,
                context,
                stepDraftKey,
                "Running",
                $"Generating content draft for '{group.Platform}' via LLM content generator...",
                cancellationToken);

            var publishingConstraint = BuildPublishingConstraint(group.Platform, context.DesiredPostType);
            var contentDraftResult = await _runtimeContentService.GeneratePostDraftAsync(
                new AgenticRuntimeContentRequest(
                    schedule.Id,
                    schedule.Name,
                    schedule.AgentPrompt,
                    group.Platform,
                    schedule.MaxContentLength,
                    enrichedSearch,
                    schedule.UserId,
                    schedule.WorkspaceId,
                    null,
                    null,
                    group.RepresentativeTarget.SocialMediaId,
                    group.Platform,
                    recommendationQuery,
                    recommendationSummary,
                    recommendationPageProfile,
                    recommendationWebSources,
                    ragFallbackReason,
                    publishingConstraint.PostType,
                    publishingConstraint.RequiresVideoMedia,
                    publishingConstraint.RequiresSingleMedia,
                    publishingConstraint.AllowTextOnly,
                    publishingConstraint.InstructionSummary),
                cancellationToken);

            if (contentDraftResult.IsFailure)
            {
                await UpdateProgressAsync(
                    schedule,
                    context,
                    stepDraftKey,
                    "Failed",
                    $"Draft generation for '{group.Platform}' failed: {contentDraftResult.Error.Description}",
                    cancellationToken);

                schedule.Status = PublishingScheduleState.StatusFailed;
                schedule.ErrorCode = contentDraftResult.Error.Code;
                schedule.ErrorMessage = contentDraftResult.Error.Description;
                schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _publishingScheduleRepository.Update(schedule);
                await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure<bool>(contentDraftResult.Error);
            }

            var validatedDraftResult = ValidateRuntimeDraft(group.Platform, publishingConstraint, contentDraftResult.Value);

            // TikTok reels fallback: if no video was found, retry as photo carousel.
            // This handles schedules created before carousel support that still have desiredPostType=reels,
            // and schedules where the AI genuinely cannot find a video URL for the topic.
            if (validatedDraftResult.IsFailure &&
                string.Equals(NormalizePlatform(group.Platform), "tiktok", StringComparison.Ordinal) &&
                string.Equals(publishingConstraint.PostType, "reels", StringComparison.Ordinal) &&
                (string.Equals(validatedDraftResult.Error.Code, "PublishingSchedule.RequiredVideoMissing", StringComparison.Ordinal) ||
                 string.Equals(validatedDraftResult.Error.Code, "PublishingSchedule.RequiredMediaMissing", StringComparison.Ordinal)))
            {
                // Log the fallback so it's visible in traces
                context = await UpdateProgressAsync(
                    schedule,
                    context,
                    stepDraftKey,
                    "Running",
                    $"TikTok reels: no video found for topic. Falling back to photo carousel (posts) with AI-generated images...",
                    cancellationToken);

                publishingConstraint = BuildPublishingConstraint(group.Platform, "posts");
                contentDraftResult = await _runtimeContentService.GeneratePostDraftAsync(
                    new AgenticRuntimeContentRequest(
                        schedule.Id,
                        schedule.Name,
                        schedule.AgentPrompt,
                        group.Platform,
                        schedule.MaxContentLength,
                        enrichedSearch,
                        schedule.UserId,
                        schedule.WorkspaceId,
                        null,
                        null,
                        group.RepresentativeTarget.SocialMediaId,
                        group.Platform,
                        recommendationQuery,
                        recommendationSummary,
                        recommendationPageProfile,
                        recommendationWebSources,
                        ragFallbackReason,
                        publishingConstraint.PostType,
                        publishingConstraint.RequiresVideoMedia,
                        publishingConstraint.RequiresSingleMedia,
                        publishingConstraint.AllowTextOnly,
                        publishingConstraint.InstructionSummary),
                    cancellationToken);

                if (contentDraftResult.IsFailure)
                {
                    await UpdateProgressAsync(
                        schedule,
                        context,
                        stepDraftKey,
                        "Failed",
                        $"Draft generation for '{group.Platform}' (carousel fallback) failed: {contentDraftResult.Error.Description}",
                        cancellationToken);

                    schedule.Status = PublishingScheduleState.StatusFailed;
                    schedule.ErrorCode = contentDraftResult.Error.Code;
                    schedule.ErrorMessage = contentDraftResult.Error.Description;
                    schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                    _publishingScheduleRepository.Update(schedule);
                    await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
                    return Result.Failure<bool>(contentDraftResult.Error);
                }

                validatedDraftResult = ValidateRuntimeDraft(group.Platform, publishingConstraint, contentDraftResult.Value);
            }

            if (validatedDraftResult.IsFailure)
            {
                await UpdateProgressAsync(
                    schedule,
                    context,
                    stepDraftKey,
                    "Failed",
                    $"Content constraint validation for '{group.Platform}' failed: {validatedDraftResult.Error.Description}",
                    cancellationToken);

                schedule.Status = PublishingScheduleState.StatusFailed;
                schedule.ErrorCode = validatedDraftResult.Error.Code;
                schedule.ErrorMessage = validatedDraftResult.Error.Description;
                schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _publishingScheduleRepository.Update(schedule);
                await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure<bool>(validatedDraftResult.Error);
            }

            context = await UpdateProgressAsync(
                schedule,
                context,
                stepDraftKey,
                "Completed",
                $"Successfully generated and validated brand content draft for '{group.Platform}'.",
                cancellationToken);

            var stepPostKey = $"post_creation_{group.Platform}";
            context = await UpdateProgressAsync(
                schedule,
                context,
                stepPostKey,
                "Running",
                $"Creating new post draft inside the system database for '{group.Platform}'...",
                cancellationToken);

            var validatedDraft = validatedDraftResult.Value;
            var importedResourceIds = validatedDraft.ResourceIds?
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Select(id => id.ToString())
                .ToList() ?? [];

            var createPostResult = await _mediator.Send(
                new CreatePostCommand(
                    schedule.UserId,
                    schedule.WorkspaceId,
                    null,
                    group.RepresentativeTarget.SocialMediaId,
                    validatedDraft.Title,
                    new PostContent
                    {
                        Content = validatedDraft.Content,
                        Hashtag = validatedDraft.Hashtag,
                        PostType = validatedDraft.PostType,
                        ResourceList = importedResourceIds
                    },
                    "draft",
                    postBuilderId,
                    group.Platform,
                    PostBuilderOriginKinds.AiOther),
                cancellationToken);

            if (createPostResult.IsFailure)
            {
                await UpdateProgressAsync(
                    schedule,
                    context,
                    stepPostKey,
                    "Failed",
                    $"Creating post draft for '{group.Platform}' failed: {createPostResult.Error.Description}",
                    cancellationToken);

                schedule.Status = PublishingScheduleState.StatusFailed;
                schedule.ErrorCode = createPostResult.Error.Code;
                schedule.ErrorMessage = createPostResult.Error.Description;
                schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _publishingScheduleRepository.Update(schedule);
                await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure<bool>(createPostResult.Error);
            }

            context = await UpdateProgressAsync(
                schedule,
                context,
                stepPostKey,
                "Completed",
                $"Post draft successfully created (PostID: {createPostResult.Value.Id}) for platform '{group.Platform}'.",
                cancellationToken);

            postBuilderId ??= createPostResult.Value.PostBuilderId;
            createdPosts.Add((createPostResult.Value, group));
        }

        var builderResourceIds = createdPosts
            .SelectMany(createdPost => createdPost.Post.Content?.ResourceList ?? [])
            .Select(ParseGuid)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (postBuilderId.HasValue && builderResourceIds.Count > 0)
        {
            context = await UpdateProgressAsync(
                schedule,
                context,
                "asset_linking",
                "Running",
                $"Linking {builderResourceIds.Count} generated resources and media assets to the Post Builder...",
                cancellationToken);

            var addBuilderResourcesResult = await _mediator.Send(
                new AddPostBuilderResourcesCommand(
                    postBuilderId.Value,
                    schedule.UserId,
                    builderResourceIds),
                cancellationToken);

            if (addBuilderResourcesResult.IsFailure)
            {
                await UpdateProgressAsync(
                    schedule,
                    context,
                    "asset_linking",
                    "Failed",
                    $"Linking assets to Post Builder failed: {addBuilderResourcesResult.Error.Description}",
                    cancellationToken);

                schedule.Status = PublishingScheduleState.StatusFailed;
                schedule.ErrorCode = addBuilderResourcesResult.Error.Code;
                schedule.ErrorMessage = addBuilderResourcesResult.Error.Description;
                schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _publishingScheduleRepository.Update(schedule);
                await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure<bool>(addBuilderResourcesResult.Error);
            }

            context = await UpdateProgressAsync(
                schedule,
                context,
                "asset_linking",
                "Completed",
                $"Linked all {builderResourceIds.Count} resources to Post Builder.",
                cancellationToken);
        }

        var activeItemCount = schedule.Items.Count(item => !item.DeletedAt.HasValue);
        var runtimeItems = new List<PublishingScheduleItem>(createdPosts.Count);
        foreach (var createdPost in createdPosts)
        {
            var runtimeItem = new PublishingScheduleItem
            {
                Id = Guid.CreateVersion7(),
                ScheduleId = schedule.Id,
                ItemType = PublishingScheduleState.ItemTypePost,
                ItemId = createdPost.Post.Id,
                SortOrder = ++activeItemCount,
                ExecutionBehavior = PublishingScheduleState.ExecutionBehaviorPublishAll,
                Status = PublishingScheduleState.ItemStatusPublishing,
                LastExecutionAt = DateTimeExtensions.PostgreSqlUtcNow,
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
                UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };
            schedule.Items.Add(runtimeItem);
            _publishingScheduleRepository.AddItem(runtimeItem);
            runtimeItems.Add(runtimeItem);
        }

        schedule.Status = PublishingScheduleState.StatusPublishing;
        context = context with
        {
            LastExecutionRunId = executionRunId,
            RuntimePostId = createdPosts.FirstOrDefault().Post.Id,
            RuntimePostBuilderId = postBuilderId,
            RuntimePostIds = createdPosts.Select(item => item.Post.Id).ToList(),
            LastExecutionStartedAtUtc = now,
            LastQuery = enrichedSearch.Query,
            LastRetrievedAtUtc = enrichedSearch.RetrievedAtUtc,
            GroundingSocialMediaId = groundingTarget.SocialMediaId,
            LastRecommendationQuery = recommendationQuery,
            LastRecommendationSummary = Truncate(recommendationSummary, 2000),
            LastRagFallbackReason = ragFallbackReason,
            LastSearchPayload = enrichedSearch
        };
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context);
        schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        context = await UpdateProgressAsync(
            schedule,
            context,
            "publishing",
            "Running",
            $"Initiating direct publishing flow of posts to {createdPosts.Count} social media target(s)...",
            cancellationToken);

        var publishResult = await _mediator.Send(
            new PublishPostsCommand(
                schedule.UserId,
                createdPosts.Select(createdPost => new PublishPostTargetInput(
                    createdPost.Post.Id,
                    createdPost.Group.Targets.Select(target => target.SocialMediaId).ToList(),
                    schedule.IsPrivate,
                    schedule.Id)).ToList()),
            cancellationToken);

        if (publishResult.IsFailure)
        {
            foreach (var runtimeItem in runtimeItems)
            {
                runtimeItem.Status = PublishingScheduleState.ItemStatusFailed;
                runtimeItem.ErrorMessage = publishResult.Error.Description;
                runtimeItem.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            }

            await UpdateProgressAsync(
                schedule,
                context,
                "publishing",
                "Failed",
                $"Direct publishing failed: {publishResult.Error.Description}",
                cancellationToken);

            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = publishResult.Error.Code;
            schedule.ErrorMessage = publishResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(publishResult.Error);
        }

        context = await UpdateProgressAsync(
            schedule,
            context,
            "publishing",
            "Completed",
            "Publishing completed successfully! The AI agent schedule run has finished.",
            cancellationToken);

        return Result.Success(true);
    }

    private static IReadOnlyList<RuntimeTargetGroup> GroupActiveTargetsByPlatform(PublishingSchedule schedule)
    {
        return schedule.Targets
            .Where(target => !target.DeletedAt.HasValue && !string.IsNullOrWhiteSpace(target.Platform))
            .GroupBy(target => target.Platform!.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var targets = group
                    .OrderByDescending(target => target.IsPrimary)
                    .ThenBy(target => target.CreatedAt ?? DateTime.MinValue)
                    .ToList();

                return new RuntimeTargetGroup(
                    group.Key,
                    targets[0],
                    targets);
            })
            .ToList();
    }

    private static PublishingScheduleTarget? ResolveGroundingTarget(PublishingSchedule schedule)
    {
        var activeTargets = schedule.Targets
            .Where(target => !target.DeletedAt.HasValue)
            .ToList();
        if (activeTargets.Count == 0)
        {
            return null;
        }

        var preferredPlatform = (schedule.PlatformPreference ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(preferredPlatform))
        {
            var platformMatches = activeTargets
                .Where(target => string.Equals(target.Platform, preferredPlatform, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(target => target.IsPrimary)
                .ToList();
            if (platformMatches.Count > 0)
            {
                return platformMatches[0];
            }
        }

        return activeTargets
            .OrderByDescending(target => target.IsPrimary)
            .FirstOrDefault();
    }

    private static Guid ParseGuid(string value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;
    }

    private static string BuildRecommendationQuery(
        PublishingSchedule schedule,
        AgentWebSearchResponse enrichedSearch)
    {
        var topResults = enrichedSearch.Results
            .Take(3)
            .Select((item, index) =>
                $"{index + 1}. {item.Title} | {item.Description} | {item.Url}")
            .ToList();

        var prompt = string.IsNullOrWhiteSpace(schedule.AgentPrompt)
            ? "Create a scheduled social post from the latest retrieved web context."
            : schedule.AgentPrompt.Trim();

        return string.Join(
            "\n",
            new[]
            {
                $"Platform preference: {schedule.PlatformPreference ?? "(none)"}",
                $"User scheduling intent: {prompt}",
                $"Fresh web topic query: {enrichedSearch.Query}",
                topResults.Count > 0
                    ? $"Top web results:\n{string.Join("\n", topResults)}"
                    : "Top web results: none",
                "Recommend one concrete post for immediate publishing that matches this account's historical voice and current web context."
            });
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "...";
    }

    private static RuntimePublishingConstraint BuildPublishingConstraint(
        string platform,
        string? desiredPostType)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedPostType = NormalizePostType(desiredPostType);

        return normalizedPlatform switch
        {
            // TikTok reels: exactly one video
            "tiktok" when normalizedPostType == "reels" => new RuntimePublishingConstraint(
                normalizedPlatform,
                "reels",
                RequiresVideoMedia: true,
                RequiresSingleMedia: true,
                AllowTextOnly: false,
                "TikTok reels require exactly one video resource."),
            // TikTok carousel posts: 1-35 images, no video
            "tiktok" => new RuntimePublishingConstraint(
                normalizedPlatform,
                "posts",
                RequiresVideoMedia: false,
                RequiresSingleMedia: false,
                AllowTextOnly: false,
                "TikTok photo posts support 1 to 35 images as a carousel. Import image URLs and use postType posts."),
            "facebook" when normalizedPostType == "reels" => new RuntimePublishingConstraint(
                normalizedPlatform,
                "reels",
                RequiresVideoMedia: true,
                RequiresSingleMedia: true,
                AllowTextOnly: false,
                "Facebook reels require exactly one video."),
            "instagram" when normalizedPostType == "reels" => new RuntimePublishingConstraint(
                normalizedPlatform,
                "reels",
                RequiresVideoMedia: true,
                RequiresSingleMedia: true,
                AllowTextOnly: false,
                "Instagram reels require exactly one video."),
            "instagram" => new RuntimePublishingConstraint(
                normalizedPlatform,
                "posts",
                RequiresVideoMedia: false,
                RequiresSingleMedia: true,
                AllowTextOnly: false,
                "Instagram posts currently require exactly one image or video."),
            "threads" => new RuntimePublishingConstraint(
                normalizedPlatform,
                "posts",
                RequiresVideoMedia: false,
                RequiresSingleMedia: true,
                AllowTextOnly: true,
                "Threads supports text-only posts or a single attached media item."),
            _ => new RuntimePublishingConstraint(
                normalizedPlatform,
                normalizedPostType,
                RequiresVideoMedia: false,
                RequiresSingleMedia: false,
                AllowTextOnly: true,
                "Facebook posts support text-only or compatible media attachments.")
        };
    }

    private static Result<AgenticRuntimePostDraft> ValidateRuntimeDraft(
        string platform,
        RuntimePublishingConstraint constraint,
        AgenticRuntimePostDraft draft)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        var normalizedPostType = NormalizePostType(draft.PostType);
        if (!string.Equals(normalizedPostType, constraint.PostType, StringComparison.Ordinal))
        {
            return Result.Failure<AgenticRuntimePostDraft>(
                new Error(
                    "PublishingSchedule.PlatformPostTypeMismatch",
                    $"The AI draft for {normalizedPlatform} must use postType '{constraint.PostType}', but got '{normalizedPostType}'."));
        }

        var resources = draft.Resources?
            .Where(resource => resource.ResourceId != Guid.Empty)
            .GroupBy(resource => resource.ResourceId)
            .Select(group => group.First())
            .ToList() ?? [];
        var videoCount = resources.Count(resource => IsVideoResource(resource.ResourceType));
        var imageCount = resources.Count(resource => IsImageResource(resource.ResourceType));
        var mediaCount = resources.Count;

        if (!constraint.AllowTextOnly && mediaCount == 0)
        {
            return Result.Failure<AgenticRuntimePostDraft>(
                new Error(
                    "PublishingSchedule.RequiredMediaMissing",
                    $"The AI draft for {normalizedPlatform} must include media that matches the target publish type."));
        }

        if (constraint.RequiresVideoMedia)
        {
            if (mediaCount != 1 || videoCount != 1)
            {
                return Result.Failure<AgenticRuntimePostDraft>(
                    new Error(
                        "PublishingSchedule.RequiredVideoMissing",
                        $"{normalizedPlatform} {constraint.PostType} publishing requires exactly one video resource."));
            }
        }
        else if (constraint.RequiresSingleMedia && mediaCount > 1)
        {
            return Result.Failure<AgenticRuntimePostDraft>(
                new Error(
                    "PublishingSchedule.SingleMediaRequired",
                    $"{normalizedPlatform} publishing currently supports only one attached media item for this target."));
        }

        // TikTok photo carousel: 1-35 images, no videos allowed
        if (string.Equals(normalizedPlatform, "tiktok", StringComparison.Ordinal) &&
            string.Equals(normalizedPostType, "posts", StringComparison.Ordinal))
        {
            if (videoCount > 0)
            {
                return Result.Failure<AgenticRuntimePostDraft>(
                    new Error("PublishingSchedule.TikTokCarouselVideoUnsupported",
                        "TikTok photo carousel posts cannot include video resources. Use postType reels for video."));
            }

            if (imageCount > 35)
            {
                return Result.Failure<AgenticRuntimePostDraft>(
                    new Error("PublishingSchedule.TikTokCarouselTooManyImages",
                        "TikTok photo carousel supports a maximum of 35 images."));
            }
        }

        if (string.Equals(normalizedPlatform, "facebook", StringComparison.Ordinal))
        {
            if (normalizedPostType == "posts")
            {
                if (videoCount > 1)
                {
                    return Result.Failure<AgenticRuntimePostDraft>(
                        new Error("PublishingSchedule.MultiVideoUnsupported", "Facebook posts support only one video."));
                }

                if (videoCount > 0 && imageCount > 0)
                {
                    return Result.Failure<AgenticRuntimePostDraft>(
                        new Error("PublishingSchedule.MixedMediaUnsupported", "Facebook posts cannot mix images and videos."));
                }
            }
        }

        return Result.Success(draft with
        {
            PostType = constraint.PostType
        });
    }

    private static bool IsVideoResource(string? resourceType)
    {
        return !string.IsNullOrWhiteSpace(resourceType) &&
               resourceType.StartsWith("video", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImageResource(string? resourceType)
    {
        return !string.IsNullOrWhiteSpace(resourceType) &&
               resourceType.StartsWith("image", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePostType(string? postType)
    {
        return string.Equals((postType ?? string.Empty).Trim(), "reels", StringComparison.OrdinalIgnoreCase)
            ? "reels"
            : "posts";
    }

    private static string NormalizePlatform(string? platform)
    {
        return (platform ?? string.Empty).Trim().ToLowerInvariant();
    }

    private sealed record RuntimeTargetGroup(
        string Platform,
        PublishingScheduleTarget RepresentativeTarget,
        IReadOnlyList<PublishingScheduleTarget> Targets);

    private sealed record RuntimePublishingConstraint(
        string Platform,
        string PostType,
        bool RequiresVideoMedia,
        bool RequiresSingleMedia,
        bool AllowTextOnly,
        string InstructionSummary);
}
