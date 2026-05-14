using Application.Abstractions.Automation;
using Application.Abstractions.Rag;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.PublishingSchedules.Models;
using Application.Recommendations.Commands;
using Application.Recommendations.Queries;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
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

    public ExecuteAgenticPublishingScheduleCommandHandler(
        IPublishingScheduleRepository publishingScheduleRepository,
        IAgenticRuntimeContentService runtimeContentService,
        IAgentWebSearchService agentWebSearchService,
        IMediator mediator,
        IRagClient ragClient)
    {
        _publishingScheduleRepository = publishingScheduleRepository;
        _runtimeContentService = runtimeContentService;
        _agentWebSearchService = agentWebSearchService;
        _mediator = mediator;
        _ragClient = ragClient;
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

        if (search is null)
        {
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
            schedule.Status = PublishingScheduleState.StatusFailed;
            schedule.ErrorCode = searchResult.Error.Code;
            schedule.ErrorMessage = searchResult.Error.Description;
            schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
            _publishingScheduleRepository.Update(schedule);
            await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
            return Result.Failure<bool>(searchResult.Error);
        }

        var enrichedSearch = searchResult.Value;
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

        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context with
        {
            LastExecutionRunId = executionRunId,
            LastExecutionStartedAtUtc = now,
            LastQuery = enrichedSearch.Query,
            LastRetrievedAtUtc = enrichedSearch.RetrievedAtUtc,
            GroundingSocialMediaId = groundingTarget?.SocialMediaId,
            LastRecommendationQuery = recommendationQuery,
            LastSearchPayload = enrichedSearch
        });
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        if (groundingTarget is null)
        {
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
            await _ragClient.WaitForRagReadyAsync(cancellationToken);

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
        }
        catch (Exception ex)
        {
            ragFallbackReason = ex.Message;
        }

        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context with
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
        });
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

        var targetGroups = GroupActiveTargetsByPlatform(schedule);
        if (targetGroups.Count == 0)
        {
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
                    ragFallbackReason),
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

            var importedResourceIds = contentDraftResult.Value.ResourceIds?
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
                    contentDraftResult.Value.Title,
                    new PostContent
                    {
                        Content = contentDraftResult.Value.Content,
                        Hashtag = contentDraftResult.Value.Hashtag,
                        PostType = contentDraftResult.Value.PostType,
                        ResourceList = importedResourceIds
                    },
                    "draft",
                    postBuilderId,
                    group.Platform,
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
            var addBuilderResourcesResult = await _mediator.Send(
                new AddPostBuilderResourcesCommand(
                    postBuilderId.Value,
                    schedule.UserId,
                    builderResourceIds),
                cancellationToken);

            if (addBuilderResourcesResult.IsFailure)
            {
                schedule.Status = PublishingScheduleState.StatusFailed;
                schedule.ErrorCode = addBuilderResourcesResult.Error.Code;
                schedule.ErrorMessage = addBuilderResourcesResult.Error.Description;
                schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
                _publishingScheduleRepository.Update(schedule);
                await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure<bool>(addBuilderResourcesResult.Error);
            }
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
            runtimeItems.Add(runtimeItem);
        }

        schedule.Status = PublishingScheduleState.StatusPublishing;
        schedule.ExecutionContextJson = AgenticScheduleExecutionContextSerializer.Serialize(context with
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
        });
        schedule.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _publishingScheduleRepository.Update(schedule);
        await _publishingScheduleRepository.SaveChangesAsync(cancellationToken);

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

    private sealed record RuntimeTargetGroup(
        string Platform,
        PublishingScheduleTarget RepresentativeTarget,
        IReadOnlyList<PublishingScheduleTarget> Targets);
}
