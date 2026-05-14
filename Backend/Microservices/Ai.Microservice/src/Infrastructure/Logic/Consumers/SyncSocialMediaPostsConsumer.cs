using Application.Abstractions.Resources;
using Application.Posts.Models;
using Application.Posts.Queries;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.Resources;
using Microsoft.Extensions.Logging;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.SocialMedia;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Consumers;

public sealed class SyncSocialMediaPostsConsumer : IConsumer<SyncSocialMediaPostsRequested>
{
    private const string PublishedStatus = "published";
    private const string ExternalContentIdType = "post_id";
    private const string PostsType = "posts";
    private const string ReelsType = "reels";
    private const int DefaultPageLimit = 50;
    private const int MaxPageLimit = 100;
    private const int DefaultMaxPages = 100;
    private const int AbsoluteMaxPages = 500;

    private readonly ISender _sender;
    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<SyncSocialMediaPostsConsumer> _logger;

    public SyncSocialMediaPostsConsumer(
        ISender sender,
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserResourceService userResourceService,
        ILogger<SyncSocialMediaPostsConsumer> logger)
    {
        _sender = sender;
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SyncSocialMediaPostsRequested> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var pageLimit = Clamp(message.PageLimit, 1, MaxPageLimit, DefaultPageLimit);
        var maxPages = Clamp(message.MaxPages, 1, AbsoluteMaxPages, DefaultMaxPages);
        var seenPostIds = new HashSet<string>(StringComparer.Ordinal);
        var syncedCount = 0;
        var failedCount = 0;
        var failures = new List<PostSyncFailure>();
        var actionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolvedPlatform = NormalizePlatform(message.Platform, message.Platform);
        string? cursor = null;

        _logger.LogInformation(
            "Social media post sync started. CorrelationId: {CorrelationId}, UserId: {UserId}, SocialMediaId: {SocialMediaId}, Platform: {Platform}",
            message.CorrelationId,
            message.UserId,
            message.SocialMediaId,
            message.Platform);

        for (var page = 1; page <= maxPages; page++)
        {
            var postsResult = await _sender.Send(
                new GetSocialMediaPlatformPostsQuery(
                    message.UserId,
                    message.SocialMediaId,
                    cursor,
                    pageLimit),
                cancellationToken);

            if (postsResult.IsFailure)
            {
                await PublishAccountSyncFailureAsync(
                    context,
                    message,
                    resolvedPlatform,
                    seenPostIds.Count,
                    syncedCount,
                    failedCount,
                    "SocialMediaPostSync.FetchFailed",
                    postsResult.Error.Description,
                    failures,
                    cancellationToken);
                return;
            }

            var response = postsResult.Value;
            resolvedPlatform = NormalizePlatform(response.Platform, message.Platform);
            foreach (var item in response.Items)
            {
                if (string.IsNullOrWhiteSpace(item.PlatformPostId) ||
                    !seenPostIds.Add(item.PlatformPostId))
                {
                    continue;
                }

                try
                {
                    var syncedPost = await SyncPostAsync(
                        message,
                        response.Platform,
                        item,
                        cancellationToken);

                    syncedCount++;
                    actionCounts[syncedPost.Action] = actionCounts.GetValueOrDefault(syncedPost.Action) + 1;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    failures.Add(new PostSyncFailure(
                        item.PlatformPostId,
                        BuildTitle(item, response.Platform),
                        ex.Message));

                    _logger.LogWarning(
                        ex,
                        "Social media post sync failed. CorrelationId: {CorrelationId}, SocialMediaId: {SocialMediaId}, PlatformPostId: {PlatformPostId}",
                        message.CorrelationId,
                        message.SocialMediaId,
                        item.PlatformPostId);
                }
            }

            if (!response.HasMore ||
                string.IsNullOrWhiteSpace(response.NextCursor) ||
                string.Equals(cursor, response.NextCursor, StringComparison.Ordinal))
            {
                break;
            }

            cursor = response.NextCursor;
        }

        _logger.LogInformation(
            "Social media post sync finished. CorrelationId: {CorrelationId}, SocialMediaId: {SocialMediaId}, SeenPosts: {SeenPosts}, SyncedPosts: {SyncedPosts}, FailedPosts: {FailedPosts}",
            message.CorrelationId,
            message.SocialMediaId,
            seenPostIds.Count,
            syncedCount,
            failedCount);

        if (failedCount == 0)
        {
            await PublishAccountSyncSuccessAsync(
                context,
                message,
                resolvedPlatform,
                seenPostIds.Count,
                syncedCount,
                actionCounts,
                cancellationToken);
        }
        else
        {
            await PublishAccountSyncFailureAsync(
                context,
                message,
                resolvedPlatform,
                seenPostIds.Count,
                syncedCount,
                failedCount,
                "SocialMediaPostSync.PartialFailure",
                $"Synced {syncedCount} {resolvedPlatform} posts, but {failedCount} posts failed.",
                failures,
                cancellationToken);
        }
    }

    private async Task<SyncedPostResult> SyncPostAsync(
        SyncSocialMediaPostsRequested message,
        string platform,
        SocialPlatformPostSummaryResponse platformPost,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var normalizedPlatform = NormalizePlatform(platform, message.Platform);
        var postType = ResolvePostType(platformPost.MediaType);
        var title = BuildTitle(platformPost, normalizedPlatform);
        var publishedAt = ToUtcDateTime(platformPost.PublishedAt) ?? now;
        var destinationOwnerId = ResolveDestinationOwnerId(normalizedPlatform, platformPost, message.SocialMediaId);

        var publication = await _postPublicationRepository.GetBySocialMediaAndExternalContentForUpdateAsync(
            message.SocialMediaId,
            platformPost.PlatformPostId,
            cancellationToken);

        if (publication is null)
        {
            publication = await _postPublicationRepository.GetByExternalContentKeyForUpdateAsync(
                normalizedPlatform,
                destinationOwnerId,
                platformPost.PlatformPostId,
                cancellationToken);
        }

        Post? post = null;
        var isExistingPublication = publication is not null;
        var action = "created";

        if (publication is not null)
        {
            post = await _postRepository.GetByIdForUpdateAsync(publication.PostId, cancellationToken);
            if (post is null)
            {
                throw new InvalidOperationException($"Existing publication points to missing post {publication.PostId}.");
            }

            if (post.UserId != message.UserId)
            {
                throw new InvalidOperationException("Existing publication belongs to a different user.");
            }

            action = post.DeletedAt.HasValue || publication.DeletedAt.HasValue
                ? "reactivated"
                : "updated";
        }
        else
        {
            post = new Post
            {
                Id = Guid.CreateVersion7(),
                UserId = message.UserId,
                WorkspaceId = null,
                CreatedAt = publishedAt
            };

            await _postRepository.AddAsync(post, cancellationToken);

            publication = new PostPublication
            {
                Id = Guid.CreateVersion7(),
                PostId = post.Id,
                WorkspaceId = Guid.Empty,
                SocialMediaId = message.SocialMediaId,
                ExternalContentId = platformPost.PlatformPostId,
                ExternalContentIdType = ExternalContentIdType,
                CreatedAt = now
            };

            await _postPublicationRepository.AddRangeAsync([publication], cancellationToken);
        }

        post.SocialMediaId = message.SocialMediaId;
        post.Platform = normalizedPlatform;
        post.Title = title;
        post.Content = BuildContent(
            platformPost,
            postType,
            await ResolveMediaResourceListAsync(
                message,
                normalizedPlatform,
                platformPost,
                postType,
                post.Content?.ResourceList,
                cancellationToken));
        post.Status = PublishedStatus;
        post.UpdatedAt = now;
        post.DeletedAt = null;

        publication.SocialMediaId = message.SocialMediaId;
        publication.SocialMediaType = normalizedPlatform;
        publication.DestinationOwnerId = destinationOwnerId;
        publication.ContentType = postType;
        publication.PublishStatus = PublishedStatus;
        publication.PublishedAt = publishedAt;
        publication.UpdatedAt = isExistingPublication ? now : publication.UpdatedAt;
        publication.DeletedAt = null;

        // Query-loaded rows and newly added rows are already tracked; SaveChanges
        // keeps inserts and updates in the correct EF state.
        await _postPublicationRepository.SaveChangesAsync(cancellationToken);

        return new SyncedPostResult(post.Id, publication.Id, action);
    }

    private static Task PublishAccountSyncSuccessAsync(
        ConsumeContext<SyncSocialMediaPostsRequested> context,
        SyncSocialMediaPostsRequested message,
        string platform,
        int seenPosts,
        int syncedPosts,
        IReadOnlyDictionary<string, int> actionCounts,
        CancellationToken cancellationToken)
    {
        return context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.SocialMediaPostSyncCompleted,
                "Published posts synced",
                seenPosts == 0
                    ? $"No published {platform} posts were found to sync."
                    : $"Synced all {syncedPosts} {platform} posts to your post library.",
                new
                {
                    message.CorrelationId,
                    message.SocialMediaId,
                    message.Trigger,
                    platform,
                    seenPosts,
                    syncedPosts,
                    failedPosts = 0,
                    actionCounts
                },
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }

    private static Task PublishAccountSyncFailureAsync(
        ConsumeContext<SyncSocialMediaPostsRequested> context,
        SyncSocialMediaPostsRequested message,
        string platform,
        int seenPosts,
        int syncedPosts,
        int failedPosts,
        string errorCode,
        string errorMessage,
        IReadOnlyList<PostSyncFailure> failures,
        CancellationToken cancellationToken)
    {
        var failureDetails = failures
            .Take(20)
            .Select(failure => new
            {
                platformPostId = failure.PlatformPostId,
                title = failure.Title,
                errorMessage = failure.ErrorMessage
            })
            .ToList();

        return context.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.SocialMediaPostSyncFailed,
                "Published posts sync failed",
                syncedPosts > 0 && failedPosts > 0
                    ? $"Synced {syncedPosts} {platform} posts, but {failedPosts} posts failed."
                    : syncedPosts > 0
                        ? $"Synced {syncedPosts} {platform} posts, but the account sync did not finish."
                    : $"Could not sync {platform} posts.",
                new
                {
                    message.CorrelationId,
                    message.SocialMediaId,
                    message.Trigger,
                    platform,
                    seenPosts,
                    syncedPosts,
                    failedPosts,
                    errorCode,
                    errorMessage,
                    failureDetails,
                    failureDetailsTruncated = failures.Count > failureDetails.Count
                },
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }

    private async Task<List<string>> ResolveMediaResourceListAsync(
        SyncSocialMediaPostsRequested message,
        string platform,
        SocialPlatformPostSummaryResponse platformPost,
        string postType,
        IReadOnlyList<string>? existingResourceList,
        CancellationToken cancellationToken)
    {
        var existingResources = NormalizeResourceList(existingResourceList);
        if (existingResources.Count > 0)
        {
            return existingResources;
        }

        var candidates = BuildMediaImportCandidates(platform, platformPost, postType);
        if (candidates.Count == 0)
        {
            return [];
        }

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                message.UserId,
                [candidate.Url],
                status: "ready",
                resourceType: candidate.ResourceType,
                cancellationToken,
                workspaceId: null,
                provenance: new ResourceProvenanceMetadata(
                    ResourceOriginKinds.SocialMediaImported,
                    OriginSourceUrl: candidate.Url));

            if (uploadResult.IsSuccess && uploadResult.Value.Count > 0)
            {
                return uploadResult.Value
                    .Select(resource => resource.ResourceId.ToString())
                    .ToList();
            }

            var error = uploadResult.IsFailure
                ? uploadResult.Error.Description
                : "No resource was created.";

            errors.Add($"{candidate.ResourceType} {candidate.Url}: {error}");
            _logger.LogWarning(
                "Social media post media import failed. CorrelationId: {CorrelationId}, SocialMediaId: {SocialMediaId}, PlatformPostId: {PlatformPostId}, ResourceType: {ResourceType}, Error: {Error}",
                message.CorrelationId,
                message.SocialMediaId,
                platformPost.PlatformPostId,
                candidate.ResourceType,
                error);
        }

        throw new InvalidOperationException(
            $"Could not download media for platform post {platformPost.PlatformPostId}. {string.Join(" | ", errors)}");
    }

    private static PostContent BuildContent(
        SocialPlatformPostSummaryResponse platformPost,
        string postType,
        List<string> resourceList)
    {
        return new PostContent
        {
            Content = FirstNonEmpty(
                platformPost.Text,
                platformPost.Description,
                platformPost.Title,
                platformPost.Permalink,
                platformPost.ShareUrl) ?? string.Empty,
            Hashtag = null,
            ResourceList = resourceList,
            PostType = postType
        };
    }

    private static List<MediaImportCandidate> BuildMediaImportCandidates(
        string platform,
        SocialPlatformPostSummaryResponse platformPost,
        string postType)
    {
        var candidates = new List<MediaImportCandidate>();
        var normalizedPlatform = NormalizePlatform(platform, platform);
        var isVideo = string.Equals(postType, ReelsType, StringComparison.Ordinal);

        if (isVideo)
        {
            AddCandidate(candidates, platformPost.VideoDownloadUrl, "video", platformPost);

            if (normalizedPlatform is "instagram" or "threads")
            {
                AddCandidate(candidates, platformPost.MediaUrl, "video", platformPost);
            }

            AddCandidate(candidates, platformPost.ThumbnailUrl, "image", platformPost);
            return candidates;
        }

        if (string.Equals(normalizedPlatform, "facebook", StringComparison.Ordinal))
        {
            AddCandidate(candidates, platformPost.ThumbnailUrl, "image", platformPost);
            AddCandidate(candidates, platformPost.MediaUrl, "image", platformPost);
            return candidates;
        }

        AddCandidate(candidates, platformPost.MediaUrl, "image", platformPost);
        AddCandidate(candidates, platformPost.ThumbnailUrl, "image", platformPost);
        return candidates;
    }

    private static void AddCandidate(
        List<MediaImportCandidate> candidates,
        string? url,
        string resourceType,
        SocialPlatformPostSummaryResponse platformPost)
    {
        if (!IsDownloadCandidateUrl(url, platformPost) ||
            candidates.Any(candidate => string.Equals(candidate.Url, url, StringComparison.Ordinal)))
        {
            return;
        }

        candidates.Add(new MediaImportCandidate(url!.Trim(), resourceType));
    }

    private static bool IsDownloadCandidateUrl(string? url, SocialPlatformPostSummaryResponse platformPost)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        return !SameUrl(url, platformPost.Permalink) &&
               !SameUrl(url, platformPost.ShareUrl) &&
               !SameUrl(url, platformPost.EmbedUrl);
    }

    private static bool SameUrl(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeResourceList(IReadOnlyList<string>? resourceList)
    {
        if (resourceList is null || resourceList.Count == 0)
        {
            return [];
        }

        return resourceList
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildTitle(SocialPlatformPostSummaryResponse platformPost, string platform)
    {
        var raw = FirstNonEmpty(platformPost.Title, platformPost.Text, platformPost.Description);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return TrimTo(raw, 140);
        }

        return $"{NormalizePlatform(platform, platform)} post";
    }

    private static string ResolvePostType(string? mediaType)
    {
        var normalized = mediaType?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return PostsType;
        }

        return normalized.Contains("video", StringComparison.Ordinal) ||
               normalized.Contains("reel", StringComparison.Ordinal)
            ? ReelsType
            : PostsType;
    }

    private static string ResolveDestinationOwnerId(
        string platform,
        SocialPlatformPostSummaryResponse platformPost,
        Guid socialMediaId)
    {
        if (string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = platformPost.PlatformPostId.IndexOf('_', StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                return platformPost.PlatformPostId[..separatorIndex];
            }
        }

        return socialMediaId.ToString();
    }

    private static string NormalizePlatform(string? value, string? fallback)
    {
        var platform = !string.IsNullOrWhiteSpace(value) ? value : fallback;
        return string.IsNullOrWhiteSpace(platform)
            ? "unknown"
            : platform.Trim().ToLowerInvariant();
    }

    private static DateTime? ToUtcDateTime(DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return DateTime.SpecifyKind(value.Value.UtcDateTime, DateTimeKind.Utc);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string TrimTo(string value, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength].TrimEnd();
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private sealed record MediaImportCandidate(string Url, string ResourceType);

    private sealed record PostSyncFailure(string PlatformPostId, string? Title, string ErrorMessage);

    private sealed record SyncedPostResult(Guid PostId, Guid PublicationId, string Action);
}
