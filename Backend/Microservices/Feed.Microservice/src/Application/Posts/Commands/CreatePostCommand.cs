using Application.Abstractions.Ai;
using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record CreatePostCommand(
    Guid UserId,
    string? Content,
    IReadOnlyCollection<Guid>? ResourceIds,
    string? MediaType,
    Guid? AiPostId = null,
    bool SkipAiMirror = false) : ICommand<PostResponse>;

public sealed class CreatePostCommandHandler : ICommandHandler<CreatePostCommand, PostResponse>
{
    private const string UnknownUsername = "unknown";
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;
    private readonly IAiFeedPostService _aiFeedPostService;
    private readonly IFeedNotificationService _feedNotificationService;

    public CreatePostCommandHandler(
        IUnitOfWork unitOfWork,
        IUserResourceService userResourceService,
        IAiFeedPostService aiFeedPostService,
        IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
        _aiFeedPostService = aiFeedPostService;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<PostResponse>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var normalizedContent = FeedPostSupport.NormalizeOptionalText(request.Content);
        var normalizedResourceIds = FeedPostSupport.NormalizeResourceIds(request.ResourceIds);
        var normalizedMediaType = FeedPostSupport.NormalizeOptionalText(request.MediaType);

        if (normalizedContent is null && normalizedResourceIds.Count == 0)
        {
            return Result.Failure<PostResponse>(FeedErrors.EmptyPost);
        }

        IReadOnlyList<UserResourcePresignResult> resources = Array.Empty<UserResourcePresignResult>();
        if (normalizedResourceIds.Count > 0)
        {
            var resourceResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                normalizedResourceIds,
                cancellationToken);

            if (resourceResult.IsFailure)
            {
                return Result.Failure<PostResponse>(resourceResult.Error);
            }

            resources = resourceResult.Value;
            if (resources.Count != normalizedResourceIds.Count)
            {
                return Result.Failure<PostResponse>(FeedErrors.MissingResources(normalizedResourceIds.Count, resources.Count));
            }
        }

        var media = resources.FirstOrDefault();
        var hashtags = FeedPostSupport.ExtractHashtags(normalizedContent);
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        if (request.AiPostId.HasValue)
        {
            var alreadyPublished = await _unitOfWork.Repository<Post>()
                .GetAll()
                .AnyAsync(
                    item =>
                        item.AiPostId == request.AiPostId.Value &&
                        !item.IsDeleted &&
                        item.DeletedAt == null,
                    cancellationToken);

            if (alreadyPublished)
            {
                return Result.Failure<PostResponse>(FeedErrors.PostAlreadyPublishedToFeed);
            }
        }

        var post = new Post
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Content = normalizedContent,
            ResourceIds = normalizedResourceIds.ToArray(),
            MediaUrl = null,
            MediaType = normalizedMediaType ?? media?.ResourceType ?? media?.ContentType,
            LikesCount = 0,
            CommentsCount = 0,
            AiPostId = request.AiPostId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _unitOfWork.Repository<Post>().AddAsync(post, cancellationToken);

        foreach (var hashtag in hashtags)
        {
            var normalizedHashtag = hashtag.Trim();
            var existingHashtag = await _unitOfWork.Repository<Hashtag>()
                .GetAll()
                .FirstOrDefaultAsync(h => h.Name == normalizedHashtag, cancellationToken);

            if (existingHashtag is null)
            {
                existingHashtag = new Hashtag
                {
                    Id = Guid.CreateVersion7(),
                    Name = normalizedHashtag,
                    PostCount = 1,
                    CreatedAt = now
                };

                await _unitOfWork.Repository<Hashtag>().AddAsync(existingHashtag, cancellationToken);
            }
            else
            {
                existingHashtag.PostCount += 1;
            }

            await _unitOfWork.Repository<PostHashtag>().AddAsync(new PostHashtag
            {
                Id = Guid.CreateVersion7(),
                PostId = post.Id,
                HashtagId = existingHashtag.Id,
                CreatedAt = now
            }, cancellationToken);
        }

        var followerIds = await _unitOfWork.Repository<Follow>()
            .GetAll()
            .Where(follow => follow.FolloweeId == request.UserId)
            .Select(follow => follow.FollowerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!request.SkipAiMirror)
        {
            var mirrorResult = await _aiFeedPostService.CreateMirrorPostAsync(
                new CreateAiMirrorPostRequest(
                    request.UserId,
                    WorkspaceId: null,
                    SocialMediaId: null,
                    Title: null,
                    Content: normalizedContent,
                    HashtagText: FeedPostSupport.BuildHashtagText(hashtags),
                    ResourceIds: normalizedResourceIds,
                    PostType: normalizedMediaType ?? (normalizedResourceIds.Count > 0 ? "media" : "text"),
                    Status: "published"),
                cancellationToken);

            if (mirrorResult.IsFailure)
            {
                return Result.Failure<PostResponse>(mirrorResult.Error);
            }

            post.AiPostId = mirrorResult.Value.PostId;
        }

        await _feedNotificationService.NotifyNewPostAsync(
            request.UserId,
            followerIds,
            post.Id,
            FeedPostSupport.BuildPreview(normalizedContent),
            cancellationToken);

        var author = new PostAuthorResponse(request.UserId, UnknownUsername, null);
        return Result.Success(PostResponseMapping.ToResponse(post, author, hashtags, resources));
    }
}

public sealed record UpdatePostCommand(
    Guid UserId,
    Guid PostId,
    string? Content,
    IReadOnlyCollection<Guid>? ResourceIds,
    string? MediaType) : ICommand<PostResponse>;

public sealed class UpdatePostCommandHandler : ICommandHandler<UpdatePostCommand, PostResponse>
{
    private const string UnknownUsername = "unknown";
    private static readonly StringComparer HashtagNameComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public UpdatePostCommandHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<PostResponse>> Handle(UpdatePostCommand request, CancellationToken cancellationToken)
    {
        var normalizedContent = FeedPostSupport.NormalizeOptionalText(request.Content);
        var normalizedResourceIds = FeedPostSupport.NormalizeResourceIds(request.ResourceIds);
        var normalizedMediaType = FeedPostSupport.NormalizeOptionalText(request.MediaType);

        if (normalizedContent is null && normalizedResourceIds.Count == 0)
        {
            return Result.Failure<PostResponse>(FeedErrors.EmptyPost);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<PostResponse>(FeedErrors.PostNotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<PostResponse>(FeedErrors.Forbidden);
        }

        IReadOnlyList<UserResourcePresignResult> resources = Array.Empty<UserResourcePresignResult>();
        if (normalizedResourceIds.Count > 0)
        {
            var resourceResult = await _userResourceService.GetPresignedResourcesAsync(
                request.UserId,
                normalizedResourceIds,
                cancellationToken);

            if (resourceResult.IsFailure)
            {
                return Result.Failure<PostResponse>(resourceResult.Error);
            }

            resources = resourceResult.Value;
            if (resources.Count != normalizedResourceIds.Count)
            {
                return Result.Failure<PostResponse>(FeedErrors.MissingResources(normalizedResourceIds.Count, resources.Count));
            }
        }

        var hashtags = FeedPostSupport.ExtractHashtags(normalizedContent);
        await ReconcilePostHashtagsAsync(post.Id, hashtags, cancellationToken);

        var media = resources.FirstOrDefault();
        post.Content = normalizedContent;
        post.ResourceIds = normalizedResourceIds.ToArray();
        post.MediaType = normalizedMediaType ?? media?.ResourceType ?? media?.ContentType;
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _unitOfWork.Repository<Post>().Update(post);

        var authorResult = await _userResourceService.GetPublicUserProfilesByIdsAsync(new[] { post.UserId }, cancellationToken);
        var author = authorResult.IsSuccess && authorResult.Value.TryGetValue(post.UserId, out var profile)
            ? new PostAuthorResponse(profile.UserId, profile.Username, profile.AvatarUrl)
            : new PostAuthorResponse(post.UserId, UnknownUsername, null);

        return Result.Success(PostResponseMapping.ToResponse(post, author, hashtags, resources, true, true));
    }

    private async Task ReconcilePostHashtagsAsync(
        Guid postId,
        IReadOnlyList<string> updatedHashtags,
        CancellationToken cancellationToken)
    {
        var hashtagRepository = _unitOfWork.Repository<Hashtag>();
        var postHashtagRepository = _unitOfWork.Repository<PostHashtag>();

        var existingPostHashtags = await postHashtagRepository
            .GetAll()
            .Where(item => item.PostId == postId)
            .ToListAsync(cancellationToken);

        var existingHashtagIds = existingPostHashtags
            .Select(item => item.HashtagId)
            .Distinct()
            .ToList();

        var existingHashtags = existingHashtagIds.Count == 0
            ? new List<Hashtag>()
            : await hashtagRepository
                .GetAll()
                .Where(item => existingHashtagIds.Contains(item.Id))
                .ToListAsync(cancellationToken);

        var existingHashtagsById = existingHashtags.ToDictionary(item => item.Id);
        var existingNames = existingPostHashtags
            .Select(item => existingHashtagsById.TryGetValue(item.HashtagId, out var hashtag) ? hashtag.Name : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(HashtagNameComparer)
            .ToList();

        var namesToRemove = existingNames
            .Except(updatedHashtags, HashtagNameComparer)
            .ToHashSet(HashtagNameComparer);

        if (namesToRemove.Count > 0)
        {
            var linksToRemove = existingPostHashtags
                .Where(item => existingHashtagsById.TryGetValue(item.HashtagId, out var hashtag) && namesToRemove.Contains(hashtag.Name))
                .ToList();

            if (linksToRemove.Count > 0)
            {
                postHashtagRepository.DeleteRange(linksToRemove);
            }

            foreach (var hashtag in existingHashtags.Where(item => namesToRemove.Contains(item.Name)))
            {
                hashtag.PostCount = Math.Max(0, hashtag.PostCount - existingPostHashtags.Count(link => link.HashtagId == hashtag.Id));
                hashtagRepository.Update(hashtag);
            }
        }

        var namesToAdd = updatedHashtags
            .Except(existingNames, HashtagNameComparer)
            .ToList();

        if (namesToAdd.Count == 0)
        {
            return;
        }

        var knownHashtags = await hashtagRepository
            .GetAll()
            .Where(item => namesToAdd.Contains(item.Name))
            .ToListAsync(cancellationToken);

        var knownHashtagsByName = knownHashtags.ToDictionary(item => item.Name, HashtagNameComparer);
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        foreach (var hashtagName in namesToAdd)
        {
            var isNewHashtag = !knownHashtagsByName.TryGetValue(hashtagName, out var hashtag);
            hashtag ??= new Hashtag
            {
                Id = Guid.CreateVersion7(),
                Name = hashtagName,
                PostCount = 0,
                CreatedAt = now
            };

            if (isNewHashtag)
            {
                await hashtagRepository.AddAsync(hashtag, cancellationToken);
                knownHashtagsByName[hashtagName] = hashtag;
            }

            hashtag.PostCount += 1;
            if (!isNewHashtag)
            {
                hashtagRepository.Update(hashtag);
            }

            await postHashtagRepository.AddAsync(new PostHashtag
            {
                Id = Guid.CreateVersion7(),
                PostId = postId,
                HashtagId = hashtag.Id,
                CreatedAt = now
            }, cancellationToken);
        }
    }
}

