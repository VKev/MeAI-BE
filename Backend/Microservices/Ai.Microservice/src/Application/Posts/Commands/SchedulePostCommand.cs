using Application.Abstractions.SocialMedias;
using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record SchedulePostCommand(
    Guid PostId,
    Guid UserId,
    PostScheduleInput Schedule) : IRequest<Result<PostResponse>>;

public sealed class SchedulePostCommandHandler
    : IRequestHandler<SchedulePostCommand, Result<PostResponse>>
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string TikTokType = "tiktok";
    private const string ThreadsType = "threads";
    private const string ScheduledStatus = "scheduled";

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly PostResponseBuilder _postResponseBuilder;

    public SchedulePostCommandHandler(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IUserSocialMediaService userSocialMediaService,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _userSocialMediaService = userSocialMediaService;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<PostResponse>> Handle(SchedulePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);
        if (post is null || post.DeletedAt.HasValue)
        {
            return Result.Failure<PostResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<PostResponse>(PostErrors.Unauthorized);
        }

        if (!post.WorkspaceId.HasValue)
        {
            return Result.Failure<PostResponse>(PostErrors.ScheduleRequiresWorkspace);
        }

        var scheduledAtUtc = NormalizeScheduledAtUtc(request.Schedule.ScheduledAtUtc);
        if (scheduledAtUtc <= DateTimeExtensions.PostgreSqlUtcNow)
        {
            return Result.Failure<PostResponse>(PostErrors.ScheduleInPast);
        }

        var socialMediaIds = request.Schedule.SocialMediaIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? [];
        if (socialMediaIds.Count == 0)
        {
            return Result.Failure<PostResponse>(PostErrors.ScheduleMissingTargets);
        }

        if (!IsSupportedPostType(post.Content?.PostType))
        {
            return Result.Failure<PostResponse>(
                new Error("Post.UnsupportedType", "Only 'posts' and 'reels' can be scheduled at the moment."));
        }

        var publications = await _postPublicationRepository.GetByPostIdForUpdateAsync(post.Id, cancellationToken);
        if (publications.Any(publication =>
                !publication.DeletedAt.HasValue &&
                !string.Equals(publication.PublishStatus, "failed", StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<PostResponse>(PostErrors.ScheduleAlreadyPublished);
        }

        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId,
            socialMediaIds,
            cancellationToken);

        if (socialMediaResult.IsFailure)
        {
            return Result.Failure<PostResponse>(socialMediaResult.Error);
        }

        var socialMediaById = socialMediaResult.Value.ToDictionary(item => item.SocialMediaId);
        foreach (var socialMediaId in socialMediaIds)
        {
            if (!socialMediaById.ContainsKey(socialMediaId))
            {
                return Result.Failure<PostResponse>(
                    new Error("SocialMedia.NotFound", "Social media account not found."));
            }
        }

        foreach (var socialMedia in socialMediaById.Values)
        {
            if (!IsSupportedSocialType(socialMedia.Type))
            {
                return Result.Failure<PostResponse>(
                    new Error(
                        "Post.InvalidSocialMedia",
                        "Only TikTok, Facebook, Instagram, or Threads social media accounts are supported for scheduling."));
            }
        }

        post.ScheduleGroupId = NormalizeGuid(request.Schedule.ScheduleGroupId) ?? post.ScheduleGroupId ?? Guid.CreateVersion7();
        post.ScheduledAtUtc = scheduledAtUtc;
        post.ScheduleTimezone = NormalizeString(request.Schedule.Timezone);
        post.ScheduledSocialMediaIds = socialMediaIds.ToArray();
        post.ScheduledIsPrivate = request.Schedule.IsPrivate;
        post.Status = ScheduledStatus;
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        var response = await _postResponseBuilder.BuildAsync(request.UserId, post, cancellationToken);
        return Result.Success(response);
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

    private static Guid? NormalizeGuid(Guid? value)
    {
        return value == Guid.Empty ? null : value;
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsSupportedPostType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "" or "post" or "posts" or "reel" or "reels" or "video";
    }

    private static bool IsSupportedSocialType(string? value)
    {
        return string.Equals(value, FacebookType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, InstagramType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, TikTokType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, ThreadsType, StringComparison.OrdinalIgnoreCase);
    }
}
