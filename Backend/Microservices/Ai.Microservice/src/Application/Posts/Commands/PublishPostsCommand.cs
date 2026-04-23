using Application.Abstractions.SocialMedias;
using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Publishing;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record PublishPostsCommand(
    Guid UserId,
    IReadOnlyList<PublishPostTargetInput> Targets) : IRequest<Result<PublishPostsResponse>>;

public sealed record PublishPostTargetInput(
    Guid PostId,
    IReadOnlyList<Guid> SocialMediaIds,
    bool? IsPrivate = null);

public sealed class PublishPostsCommandHandler
    : IRequestHandler<PublishPostsCommand, Result<PublishPostsResponse>>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";
    private const string PostsType = "posts";
    private const string ProcessingStatus = "processing";

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IBus _bus;

    public PublishPostsCommandHandler(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserSocialMediaService userSocialMediaService,
        IBus bus)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userSocialMediaService = userSocialMediaService;
        _bus = bus;
    }

    public async Task<Result<PublishPostsResponse>> Handle(
        PublishPostsCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedTargetsResult = NormalizeTargets(request.Targets);
        if (normalizedTargetsResult.IsFailure)
        {
            return Result.Failure<PublishPostsResponse>(normalizedTargetsResult.Error);
        }

        var normalizedTargets = normalizedTargetsResult.Value;
        var socialMediaByIdResult = await GetSocialMediaByIdAsync(
            request.UserId,
            normalizedTargets,
            cancellationToken);

        if (socialMediaByIdResult.IsFailure)
        {
            return Result.Failure<PublishPostsResponse>(socialMediaByIdResult.Error);
        }

        var socialMediaById = socialMediaByIdResult.Value;
        var responses = new List<PublishPostResponse>(normalizedTargets.Count);
        var messagesToPublish = new List<PublishToTargetRequested>();
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var correlationId = Guid.CreateVersion7();

        foreach (var target in normalizedTargets)
        {
            var post = await _postRepository.GetByIdForUpdateAsync(target.PostId, cancellationToken);
            if (post == null || post.DeletedAt.HasValue)
            {
                return Result.Failure<PublishPostsResponse>(
                    new Error("Post.NotFound", "Post not found."));
            }

            if (post.UserId != request.UserId)
            {
                return Result.Failure<PublishPostsResponse>(
                    new Error("Post.Unauthorized", "You are not authorized to publish this post."));
            }

            if (!post.WorkspaceId.HasValue)
            {
                return Result.Failure<PublishPostsResponse>(PostErrors.WorkspaceIdRequired);
            }

            var postType = post.Content?.PostType ?? PostsType;
            if (!string.Equals(postType, PostsType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(postType, "reels", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<PublishPostsResponse>(
                    new Error("Post.UnsupportedType", "Only 'posts' and 'reels' can be published at the moment."));
            }

            // Clean up stale FAILED publications from previous attempts on this post before
            // creating the new placeholders. Without this, the batch_completed notification
            // groups by (SocialMediaId, DestinationOwnerId) and mixes old failed rows
            // (destinationOwnerId = socialMediaId string) with the new successful rows
            // (destinationOwnerId = real pageId) — producing 2× the chip count when a
            // prior attempt failed (e.g. MixedMedia rejection).
            var existingPublications = await _postPublicationRepository
                .GetByPostIdForUpdateAsync(post.Id, cancellationToken);
            foreach (var stale in existingPublications)
            {
                if (stale.DeletedAt.HasValue) continue;
                if (string.Equals(stale.PublishStatus, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    stale.DeletedAt = now;
                    stale.UpdatedAt = now;
                    _postPublicationRepository.Update(stale);
                }
            }

            var destinationResults = new List<PublishPostDestinationResult>();
            var placeholders = new List<PostPublication>();

            foreach (var socialMediaId in target.SocialMediaIds)
            {
                var socialMedia = socialMediaById[socialMediaId];
                var publicationId = Guid.CreateVersion7();
                var placeholder = new PostPublication
                {
                    Id = publicationId,
                    PostId = post.Id,
                    WorkspaceId = post.WorkspaceId!.Value,
                    SocialMediaId = socialMediaId,
                    SocialMediaType = socialMedia.Type,
                    DestinationOwnerId = socialMediaId.ToString(),
                    ExternalContentId = $"pending:{publicationId:N}",
                    ExternalContentIdType = string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase)
                        ? "publish_id"
                        : "post_id",
                    ContentType = postType,
                    PublishStatus = ProcessingStatus,
                    CreatedAt = now
                };

                placeholders.Add(placeholder);

                destinationResults.Add(new PublishPostDestinationResult(
                    socialMediaId,
                    socialMedia.Type,
                    string.Empty,
                    string.Empty,
                    publicationId,
                    ProcessingStatus));

                messagesToPublish.Add(new PublishToTargetRequested
                {
                    CorrelationId = correlationId,
                    UserId = request.UserId,
                    WorkspaceId = post.WorkspaceId!.Value,
                    PostId = post.Id,
                    SocialMediaId = socialMediaId,
                    PublicationId = publicationId,
                    SocialMediaType = socialMedia.Type,
                    IsPrivate = target.IsPrivate,
                    AttemptNumber = 1,
                    CreatedAt = now
                });
            }

            await _postPublicationRepository.AddRangeAsync(placeholders, cancellationToken);

            ClearSchedule(post);
            post.Status = ProcessingStatus;
            post.UpdatedAt = now;
            _postRepository.Update(post);

            responses.Add(new PublishPostResponse(
                post.Id,
                ProcessingStatus,
                destinationResults));
        }

        await _postRepository.SaveChangesAsync(cancellationToken);

        foreach (var message in messagesToPublish)
        {
            await _bus.Publish(message, cancellationToken);
        }

        return Result.Success(new PublishPostsResponse(responses));
    }

    private async Task<Result<IReadOnlyDictionary<Guid, UserSocialMediaResult>>> GetSocialMediaByIdAsync(
        Guid userId,
        IReadOnlyList<PublishPostTargetInput> targets,
        CancellationToken cancellationToken)
    {
        var allSocialMediaIds = targets
            .SelectMany(target => target.SocialMediaIds)
            .Distinct()
            .ToList();

        var socialMediasResult = await _userSocialMediaService.GetSocialMediasAsync(
            userId,
            allSocialMediaIds,
            cancellationToken);

        if (socialMediasResult.IsFailure)
        {
            return Result.Failure<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(socialMediasResult.Error);
        }

        var socialMediaById = socialMediasResult.Value.ToDictionary(item => item.SocialMediaId);

        foreach (var socialMediaId in allSocialMediaIds)
        {
            if (!socialMediaById.ContainsKey(socialMediaId))
            {
                return Result.Failure<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(
                    new Error("SocialMedia.NotFound", "Social media account not found."));
            }
        }

        foreach (var socialMedia in socialMediaById.Values)
        {
            if (!string.Equals(socialMedia.Type, FacebookType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(socialMedia.Type, InstagramType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(socialMedia.Type, TikTokType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(socialMedia.Type, ThreadsType, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(
                    new Error(
                        "Post.InvalidSocialMedia",
                        "Only TikTok, Facebook, Instagram, or Threads social media accounts are supported for posts."));
            }
        }

        return Result.Success<IReadOnlyDictionary<Guid, UserSocialMediaResult>>(socialMediaById);
    }

    private static Result<IReadOnlyList<PublishPostTargetInput>> NormalizeTargets(
        IReadOnlyList<PublishPostTargetInput>? targets)
    {
        if (targets is null || targets.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                new Error("Post.PublishMissingTargets", "At least one publish target is required."));
        }

        var normalizedByPostId = new Dictionary<Guid, NormalizedPublishTarget>();

        foreach (var target in targets)
        {
            if (target.PostId == Guid.Empty)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishMissingPostId", "Each publish target must include a valid postId."));
            }

            var socialMediaIds = target.SocialMediaIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (socialMediaIds.Count == 0)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishMissingSocialMedia", "Each publish target must include at least one social media id."));
            }

            if (normalizedByPostId.TryGetValue(target.PostId, out var existing) &&
                existing.IsPrivate.HasValue &&
                target.IsPrivate.HasValue &&
                existing.IsPrivate.Value != target.IsPrivate.Value)
            {
                return Result.Failure<IReadOnlyList<PublishPostTargetInput>>(
                    new Error("Post.PublishPrivacyConflict", "Conflicting isPrivate values were provided for the same post."));
            }

            if (!normalizedByPostId.TryGetValue(target.PostId, out existing))
            {
                existing = new NormalizedPublishTarget(target.IsPrivate);
                normalizedByPostId[target.PostId] = existing;
            }
            else if (!existing.IsPrivate.HasValue && target.IsPrivate.HasValue)
            {
                existing.IsPrivate = target.IsPrivate.Value;
            }

            foreach (var socialMediaId in socialMediaIds)
            {
                existing.SocialMediaIds.Add(socialMediaId);
            }
        }

        return Result.Success<IReadOnlyList<PublishPostTargetInput>>(
            normalizedByPostId
                .Select(item => new PublishPostTargetInput(
                    item.Key,
                    item.Value.SocialMediaIds.ToList(),
                    item.Value.IsPrivate))
                .ToList());
    }

    private sealed class NormalizedPublishTarget
    {
        public NormalizedPublishTarget(bool? isPrivate)
        {
            IsPrivate = isPrivate;
        }

        public HashSet<Guid> SocialMediaIds { get; } = [];

        public bool? IsPrivate { get; set; }
    }

    private static void ClearSchedule(Post post)
    {
        post.ScheduleGroupId = null;
        post.ScheduledSocialMediaIds = Array.Empty<Guid>();
        post.ScheduledIsPrivate = null;
        post.ScheduleTimezone = null;
        post.ScheduledAtUtc = null;
    }
}
