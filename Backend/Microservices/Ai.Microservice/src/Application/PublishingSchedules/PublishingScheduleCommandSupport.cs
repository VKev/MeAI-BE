using Application.Abstractions.SocialMedias;
using Application.PublishingSchedules.Models;
using Application.Posts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.PublishingSchedules;

internal sealed class PublishingScheduleCommandSupport
{
    private static readonly StringComparer PlatformComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserSocialMediaService _userSocialMediaService;

    public PublishingScheduleCommandSupport(
        IWorkspaceRepository workspaceRepository,
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserSocialMediaService userSocialMediaService)
    {
        _workspaceRepository = workspaceRepository;
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userSocialMediaService = userSocialMediaService;
    }

    public async Task<Result<ValidatedPublishingScheduleData>> ValidateAsync(
        Guid userId,
        Guid workspaceId,
        string? name,
        string? mode,
        DateTime executeAtUtc,
        string? timezone,
        bool? isPrivate,
        string? platformPreference,
        string? agentPrompt,
        PublishingScheduleSearchInput? search,
        IReadOnlyList<PublishingScheduleItemInput>? items,
        IReadOnlyList<PublishingScheduleTargetInput>? targets,
        Guid? currentScheduleId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.NameRequired);
        }

        if (!await _workspaceRepository.ExistsForUserAsync(workspaceId, userId, cancellationToken))
        {
            return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.WorkspaceNotFound);
        }

        var normalizedMode = NormalizeMode(mode);
        if (!string.Equals(normalizedMode, PublishingScheduleState.FixedContentMode, StringComparison.Ordinal) &&
            !string.Equals(normalizedMode, PublishingScheduleState.AgenticMode, StringComparison.Ordinal))
        {
            return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.UnsupportedMode);
        }

        var normalizedTimezone = NormalizeString(timezone);
        if (!IsValidTimezone(normalizedTimezone))
        {
            return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.InvalidTimezone);
        }

        var normalizedExecuteAtUtc = NormalizeScheduledAtUtc(executeAtUtc);
        if (normalizedExecuteAtUtc <= DateTimeExtensions.PostgreSqlUtcNow)
        {
            return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.ExecuteAtInPast);
        }

        var normalizedItems = string.Equals(normalizedMode, PublishingScheduleState.AgenticMode, StringComparison.Ordinal)
            ? Result.Success<IReadOnlyList<NormalizedPublishingScheduleItem>>(Array.Empty<NormalizedPublishingScheduleItem>())
            : NormalizeItems(items);
        if (normalizedItems.IsFailure)
        {
            return Result.Failure<ValidatedPublishingScheduleData>(normalizedItems.Error);
        }

        var normalizedSearch = string.Equals(normalizedMode, PublishingScheduleState.AgenticMode, StringComparison.Ordinal)
            ? NormalizeSearch(search)
            : Result.Success<NormalizedPublishingScheduleSearch?>(null);
        if (normalizedSearch.IsFailure)
        {
            return Result.Failure<ValidatedPublishingScheduleData>(normalizedSearch.Error);
        }

        var normalizedAgentPrompt = NormalizeString(agentPrompt);
        if (string.Equals(normalizedMode, PublishingScheduleState.AgenticMode, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(normalizedAgentPrompt))
        {
            return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.AgentPromptRequired);
        }

        var normalizedTargets = NormalizeTargets(targets);
        if (normalizedTargets.IsFailure)
        {
            return Result.Failure<ValidatedPublishingScheduleData>(normalizedTargets.Error);
        }

        var targetSocialMediaIds = normalizedTargets.Value
            .Select(target => target.SocialMediaId)
            .Distinct()
            .ToList();

        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            userId,
            targetSocialMediaIds,
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<ValidatedPublishingScheduleData>(socialMediaResult.Error);
        }

        var socialMediaById = socialMediaResult.Value.ToDictionary(item => item.SocialMediaId);
        foreach (var socialMediaId in targetSocialMediaIds)
        {
            if (!socialMediaById.ContainsKey(socialMediaId))
            {
                return Result.Failure<ValidatedPublishingScheduleData>(
                    new Error("SocialMedia.NotFound", "Social media account not found."));
            }
        }

        foreach (var socialMedia in socialMediaById.Values)
        {
            if (!IsSupportedSocialType(socialMedia.Type))
            {
                return Result.Failure<ValidatedPublishingScheduleData>(
                    new Error(
                        "Post.InvalidSocialMedia",
                        "Only TikTok, Facebook, Instagram, or Threads social media accounts are supported for scheduling."));
            }
        }

        var postIds = normalizedItems.Value.Select(item => item.ItemId).Distinct().ToList();
        var posts = await _postRepository.GetByIdsForUpdateAsync(postIds, cancellationToken);
        var postsById = posts.ToDictionary(post => post.Id);

        foreach (var postId in postIds)
        {
            if (!postsById.ContainsKey(postId))
            {
                return Result.Failure<ValidatedPublishingScheduleData>(PostErrors.NotFound);
            }
        }

        foreach (var post in posts)
        {
            if (post.DeletedAt.HasValue || post.UserId != userId)
            {
                return Result.Failure<ValidatedPublishingScheduleData>(PostErrors.NotFound);
            }

            if (post.WorkspaceId != workspaceId)
            {
                return Result.Failure<ValidatedPublishingScheduleData>(PublishingScheduleErrors.WorkspaceNotFound);
            }

            if (!IsSupportedPostType(post.Content?.PostType))
            {
                return Result.Failure<ValidatedPublishingScheduleData>(
                    new Error("Post.UnsupportedType", "Only 'posts' and 'reels' can be scheduled at the moment."));
            }

            if (post.ScheduleGroupId.HasValue &&
                post.ScheduleGroupId != currentScheduleId &&
                post.ScheduledAtUtc.HasValue &&
                string.Equals(post.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<ValidatedPublishingScheduleData>(
                    new Error("PublishingSchedule.PostAlreadyScheduled", "One or more posts already belong to another active schedule."));
            }
        }

        var publications = await _postPublicationRepository.GetByPostIdsAsync(postIds, cancellationToken);
        foreach (var postId in postIds)
        {
            if (publications.Any(publication =>
                    publication.PostId == postId &&
                    !publication.DeletedAt.HasValue &&
                    !string.Equals(publication.PublishStatus, "failed", StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Failure<ValidatedPublishingScheduleData>(PostErrors.ScheduleAlreadyPublished);
            }
        }

        return Result.Success(new ValidatedPublishingScheduleData(
            NormalizeString(name),
            normalizedMode,
            normalizedExecuteAtUtc,
            normalizedTimezone,
            isPrivate,
            NormalizeString(platformPreference),
            normalizedAgentPrompt,
            normalizedSearch.Value,
            normalizedItems.Value,
            normalizedTargets.Value,
            posts,
            socialMediaById));
    }

    public static void ApplyPostScheduling(
        Guid scheduleId,
        DateTime executeAtUtc,
        string? timezone,
        IReadOnlyList<Guid> targetSocialMediaIds,
        bool? isPrivate,
        IEnumerable<Post> posts)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var post in posts)
        {
            post.ScheduleGroupId = scheduleId;
            post.ScheduledAtUtc = executeAtUtc;
            post.ScheduleTimezone = timezone;
            post.ScheduledSocialMediaIds = targetSocialMediaIds.ToArray();
            post.ScheduledIsPrivate = isPrivate;
            post.Status = "scheduled";
            post.UpdatedAt = now;
        }
    }

    public static void ClearPostScheduling(Guid scheduleId, IEnumerable<Post> posts)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var post in posts)
        {
            if (post.ScheduleGroupId != scheduleId)
            {
                continue;
            }

            post.ScheduleGroupId = null;
            post.ScheduledAtUtc = null;
            post.ScheduleTimezone = null;
            post.ScheduledSocialMediaIds = Array.Empty<Guid>();
            post.ScheduledIsPrivate = null;
            post.UpdatedAt = now;
        }
    }

    private static Result<IReadOnlyList<NormalizedPublishingScheduleItem>> NormalizeItems(
        IReadOnlyList<PublishingScheduleItemInput>? items)
    {
        if (items is null || items.Count == 0)
        {
            return Result.Failure<IReadOnlyList<NormalizedPublishingScheduleItem>>(PublishingScheduleErrors.MissingItems);
        }

        var normalizedItems = new List<NormalizedPublishingScheduleItem>(items.Count);
        var seenPostIds = new HashSet<Guid>();

        foreach (var item in items)
        {
            var itemId = item.ItemId;
            if (itemId == Guid.Empty)
            {
                return Result.Failure<IReadOnlyList<NormalizedPublishingScheduleItem>>(PostErrors.NotFound);
            }

            var itemType = NormalizeItemType(item.ItemType);
            if (!string.Equals(itemType, PublishingScheduleState.ItemTypePost, StringComparison.Ordinal))
            {
                return Result.Failure<IReadOnlyList<NormalizedPublishingScheduleItem>>(PublishingScheduleErrors.UnsupportedItemType);
            }

            if (!seenPostIds.Add(itemId))
            {
                return Result.Failure<IReadOnlyList<NormalizedPublishingScheduleItem>>(
                    new Error("PublishingSchedule.DuplicateItems", "A post can only appear once in a schedule."));
            }

            normalizedItems.Add(new NormalizedPublishingScheduleItem(
                itemType,
                itemId,
                item.SortOrder ?? normalizedItems.Count + 1,
                NormalizeExecutionBehavior(item.ExecutionBehavior)));
        }

        return Result.Success<IReadOnlyList<NormalizedPublishingScheduleItem>>(
            normalizedItems
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.ItemId)
                .Select((item, index) => item with { SortOrder = index + 1 })
                .ToList());
    }

    private static Result<IReadOnlyList<NormalizedPublishingScheduleTarget>> NormalizeTargets(
        IReadOnlyList<PublishingScheduleTargetInput>? targets)
    {
        if (targets is null || targets.Count == 0)
        {
            return Result.Failure<IReadOnlyList<NormalizedPublishingScheduleTarget>>(PublishingScheduleErrors.MissingTargets);
        }

        var targetBySocialId = new Dictionary<Guid, NormalizedPublishingScheduleTarget>();
        foreach (var target in targets)
        {
            if (target.SocialMediaId == Guid.Empty)
            {
                return Result.Failure<IReadOnlyList<NormalizedPublishingScheduleTarget>>(PublishingScheduleErrors.MissingTargets);
            }

            if (!targetBySocialId.TryGetValue(target.SocialMediaId, out var existing))
            {
                existing = new NormalizedPublishingScheduleTarget(target.SocialMediaId, target.IsPrimary ?? false);
                targetBySocialId[target.SocialMediaId] = existing;
                continue;
            }

            if (target.IsPrimary == true)
            {
                targetBySocialId[target.SocialMediaId] = existing with { IsPrimary = true };
            }
        }

        var normalizedTargets = targetBySocialId.Values.ToList();
        if (!normalizedTargets.Any(target => target.IsPrimary))
        {
            normalizedTargets[0] = normalizedTargets[0] with { IsPrimary = true };
        }

        return Result.Success<IReadOnlyList<NormalizedPublishingScheduleTarget>>(normalizedTargets);
    }

    private static bool IsValidTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool IsSupportedPostType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "" or "post" or "posts" or "reel" or "reels" or "video";
    }

    private static bool IsSupportedSocialType(string? value)
    {
        return PlatformComparer.Equals(value, "facebook") ||
               PlatformComparer.Equals(value, "instagram") ||
               PlatformComparer.Equals(value, "tiktok") ||
               PlatformComparer.Equals(value, "threads");
    }

    private static string NormalizeMode(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => PublishingScheduleState.FixedContentMode,
            "fixed" => PublishingScheduleState.FixedContentMode,
            "agent" => PublishingScheduleState.AgenticMode,
            "agentic_live_content_schedule" => PublishingScheduleState.AgenticMode,
            _ => (value ?? string.Empty).Trim().ToLowerInvariant()
        };
    }

    private static string NormalizeItemType(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => PublishingScheduleState.ItemTypePost,
            "posts" => PublishingScheduleState.ItemTypePost,
            _ => (value ?? string.Empty).Trim().ToLowerInvariant()
        };
    }

    private static string NormalizeExecutionBehavior(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => PublishingScheduleState.ExecutionBehaviorPublishAll,
            _ => (value ?? string.Empty).Trim().ToLowerInvariant()
        };
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime NormalizeScheduledAtUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static Result<NormalizedPublishingScheduleSearch?> NormalizeSearch(
        PublishingScheduleSearchInput? search)
    {
        if (search is null)
        {
            return Result.Failure<NormalizedPublishingScheduleSearch?>(PublishingScheduleErrors.SearchConfigRequired);
        }

        var queryTemplate = NormalizeString(search.QueryTemplate);
        if (string.IsNullOrWhiteSpace(queryTemplate))
        {
            return Result.Failure<NormalizedPublishingScheduleSearch?>(PublishingScheduleErrors.SearchQueryTemplateRequired);
        }

        var count = Math.Clamp(search.Count ?? 5, 1, 10);

        return Result.Success<NormalizedPublishingScheduleSearch?>(new NormalizedPublishingScheduleSearch(
            queryTemplate,
            count,
            NormalizeString(search.Country),
            NormalizeString(search.SearchLanguage),
            NormalizeString(search.Freshness)));
    }
}

internal sealed record ValidatedPublishingScheduleData(
    string? Name,
    string Mode,
    DateTime ExecuteAtUtc,
    string? Timezone,
    bool? IsPrivate,
    string? PlatformPreference,
    string? AgentPrompt,
    NormalizedPublishingScheduleSearch? Search,
    IReadOnlyList<NormalizedPublishingScheduleItem> Items,
    IReadOnlyList<NormalizedPublishingScheduleTarget> Targets,
    IReadOnlyList<Post> Posts,
    IReadOnlyDictionary<Guid, UserSocialMediaResult> SocialMediaById);

internal sealed record NormalizedPublishingScheduleItem(
    string ItemType,
    Guid ItemId,
    int SortOrder,
    string ExecutionBehavior);

internal sealed record NormalizedPublishingScheduleTarget(
    Guid SocialMediaId,
    bool IsPrimary);

internal sealed record NormalizedPublishingScheduleSearch(
    string QueryTemplate,
    int Count,
    string? Country,
    string? SearchLanguage,
    string? Freshness);
