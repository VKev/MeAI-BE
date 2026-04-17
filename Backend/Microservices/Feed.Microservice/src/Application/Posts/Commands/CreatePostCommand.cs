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
    string? MediaType) : ICommand<PostResponse>;

public sealed class CreatePostCommandHandler : ICommandHandler<CreatePostCommand, PostResponse>
{
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
            SharesCount = 0,
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
                    PostCount = 0,
                    CreatedAt = now
                };

                await _unitOfWork.Repository<Hashtag>().AddAsync(existingHashtag, cancellationToken);
            }

            existingHashtag.PostCount += 1;
            _unitOfWork.Repository<Hashtag>().Update(existingHashtag);

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

        await _feedNotificationService.NotifyNewPostAsync(
            request.UserId,
            followerIds,
            post.Id,
            FeedPostSupport.BuildPreview(normalizedContent),
            cancellationToken);

        return Result.Success(PostResponseMapping.ToResponse(post, hashtags, resources));
    }
}
