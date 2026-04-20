using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Comments.Models;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Application.Common;

internal static partial class FeedPostSupport
{
    private static readonly Regex HashtagRegex = HashtagRegexFactory();
    private const string UnknownUsername = "unknown";

    public static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string NormalizeRequiredText(string value)
    {
        return value.Trim();
    }

    public static IReadOnlyList<Guid> NormalizeResourceIds(IReadOnlyCollection<Guid>? resourceIds)
    {
        return (resourceIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    public static IReadOnlyList<string> ExtractHashtags(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        return HashtagRegex.Matches(content)
            .Select(match => match.Value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? BuildHashtagText(IReadOnlyList<string> hashtags)
    {
        return hashtags.Count == 0 ? null : string.Join(' ', hashtags);
    }

    public static string BuildPreview(string? value, int maxLength = 120)
    {
        var normalized = NormalizeOptionalText(value);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }

    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> LoadHashtagsByPostIdsAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var ids = postIds.Distinct().ToList();

        var rows = await (
                from postHashtag in unitOfWork.Repository<PostHashtag>().GetAll()
                join hashtag in unitOfWork.Repository<Hashtag>().GetAll() on postHashtag.HashtagId equals hashtag.Id
                where ids.Contains(postHashtag.PostId)
                select new { postHashtag.PostId, hashtag.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.PostId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(item => item.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<UserResourcePresignResult>>> LoadPresignedMediaByPostIdsAsync(
        IUserResourceService userResourceService,
        IReadOnlyCollection<Post> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<UserResourcePresignResult>>();
        }

        var resourceIds = posts
            .SelectMany(post => post.ResourceIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (resourceIds.Count == 0)
        {
            return posts.ToDictionary(post => post.Id, _ => (IReadOnlyList<UserResourcePresignResult>)Array.Empty<UserResourcePresignResult>());
        }

        var presignResult = await userResourceService.GetPublicPresignedResourcesAsync(resourceIds, cancellationToken);
        if (presignResult.IsFailure)
        {
            return posts.ToDictionary(post => post.Id, _ => (IReadOnlyList<UserResourcePresignResult>)Array.Empty<UserResourcePresignResult>());
        }

        var resourcesById = presignResult.Value.ToDictionary(item => item.ResourceId, item => item);

        return posts.ToDictionary(
            post => post.Id,
            post => (IReadOnlyList<UserResourcePresignResult>)(post.ResourceIds ?? Array.Empty<Guid>())
                .Where(resourcesById.ContainsKey)
                .Select(resourceId => resourcesById[resourceId])
                .ToList());
    }

    public static async Task<IReadOnlySet<Guid>> LoadLikedPostIdsByUserAsync(
        IUnitOfWork unitOfWork,
        Guid? requesterUserId,
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (!requesterUserId.HasValue || requesterUserId.Value == Guid.Empty)
        {
            return new HashSet<Guid>();
        }

        if (postIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = postIds.Distinct().ToHashSet();

        var likedPostIds = await unitOfWork.Repository<PostLike>()
            .GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == requesterUserId.Value && ids.Contains(item.PostId))
            .Select(item => item.PostId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return likedPostIds.Count == 0
            ? new HashSet<Guid>()
            : likedPostIds.ToHashSet();
    }

    public static async Task<IReadOnlySet<Guid>> LoadLikedCommentIdsByUserAsync(
        IUnitOfWork unitOfWork,
        Guid? requesterUserId,
        IReadOnlyCollection<Guid> commentIds,
        CancellationToken cancellationToken)
    {
        if (!requesterUserId.HasValue || requesterUserId.Value == Guid.Empty)
        {
            return new HashSet<Guid>();
        }

        if (commentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = commentIds.Distinct().ToHashSet();

        var likedCommentIds = await unitOfWork.Repository<CommentLike>()
            .GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == requesterUserId.Value && ids.Contains(item.CommentId))
            .Select(item => item.CommentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return likedCommentIds.Count == 0
            ? new HashSet<Guid>()
            : likedCommentIds.ToHashSet();
    }

    public static async Task<IReadOnlyDictionary<Guid, CommentAuthorResponse>> LoadCommentAuthorsByUserIdsAsync(
        IUserResourceService userResourceService,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var distinctUserIds = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctUserIds.Count == 0)
        {
            return new Dictionary<Guid, CommentAuthorResponse>();
        }

        var profilesResult = await userResourceService.GetPublicUserProfilesByIdsAsync(distinctUserIds, cancellationToken);
        if (profilesResult.IsFailure)
        {
            return distinctUserIds.ToDictionary(id => id, CreateFallbackCommentAuthor);
        }

        return distinctUserIds.ToDictionary(
            id => id,
            id => profilesResult.Value.TryGetValue(id, out var profile)
                ? new CommentAuthorResponse(profile.UserId, profile.Username, profile.AvatarUrl)
                : CreateFallbackCommentAuthor(id));
    }

    public static async Task<IReadOnlyList<CommentResponse>> ToCommentResponsesAsync(
        IUnitOfWork unitOfWork,
        IUserResourceService userResourceService,
        Guid? requesterUserId,
        IReadOnlyList<Comment> comments,
        Func<Comment, bool?> canDeleteFactory,
        CancellationToken cancellationToken)
    {
        if (comments.Count == 0)
        {
            return Array.Empty<CommentResponse>();
        }

        var commentIds = comments
            .Select(comment => comment.Id)
            .Distinct()
            .ToList();

        var likedCommentIds = await LoadLikedCommentIdsByUserAsync(
            unitOfWork,
            requesterUserId,
            commentIds,
            cancellationToken);

        var authorsByUserId = await LoadCommentAuthorsByUserIdsAsync(
            userResourceService,
            comments.Select(comment => comment.UserId).ToList(),
            cancellationToken);

        return comments
            .Select(comment =>
            {
                bool? isLikedByCurrentUser = requesterUserId.HasValue ? likedCommentIds.Contains(comment.Id) : null;
                var author = authorsByUserId.TryGetValue(comment.UserId, out var authorResponse)
                    ? authorResponse
                    : CreateFallbackCommentAuthor(comment.UserId);

                return CommentResponseMapping.ToResponse(
                    comment,
                    author,
                    isLikedByCurrentUser,
                    canDeleteFactory(comment));
            })
            .ToList();
    }

    public static async Task<CommentResponse> ToCommentResponseAsync(
        IUserResourceService userResourceService,
        Guid? requesterUserId,
        Comment comment,
        bool? canDelete,
        CancellationToken cancellationToken)
    {
        var authorsByUserId = await LoadCommentAuthorsByUserIdsAsync(
            userResourceService,
            new[] { comment.UserId },
            cancellationToken);

        var author = authorsByUserId.TryGetValue(comment.UserId, out var authorResponse)
            ? authorResponse
            : CreateFallbackCommentAuthor(comment.UserId);

        return CommentResponseMapping.ToResponse(
            comment,
            author,
            requesterUserId.HasValue ? false : null,
            canDelete);
    }

    public static async Task<IReadOnlyList<PostResponse>> ToPostResponsesAsync(
        IUnitOfWork unitOfWork,
        IUserResourceService userResourceService,
        Guid? requesterUserId,
        IReadOnlyList<Post> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return Array.Empty<PostResponse>();
        }

        var postIds = posts
            .Select(post => post.Id)
            .Distinct()
            .ToList();

        var hashtags = await LoadHashtagsByPostIdsAsync(
            unitOfWork,
            postIds,
            cancellationToken);

        var mediaByPostId = await LoadPresignedMediaByPostIdsAsync(
            userResourceService,
            posts,
            cancellationToken);

        var likedPostIds = await LoadLikedPostIdsByUserAsync(
            unitOfWork,
            requesterUserId,
            postIds,
            cancellationToken);

        var authorsByUserId = await LoadPostAuthorsByUserIdsAsync(
            userResourceService,
            posts.Select(post => post.UserId).ToList(),
            cancellationToken);

        return posts
            .Select(post =>
            {
                bool? isLikedByCurrentUser = requesterUserId.HasValue ? likedPostIds.Contains(post.Id) : null;
                bool? canDelete = requesterUserId.HasValue ? post.UserId == requesterUserId.Value : null;
                var author = authorsByUserId.TryGetValue(post.UserId, out var authorResponse)
                    ? authorResponse
                    : CreateFallbackAuthor(post.UserId);

                return PostResponseMapping.ToResponse(
                    post,
                    author,
                    hashtags.TryGetValue(post.Id, out var hashtagValues) ? hashtagValues : Array.Empty<string>(),
                    mediaByPostId.TryGetValue(post.Id, out var mediaValues) ? mediaValues : Array.Empty<UserResourcePresignResult>(),
                    isLikedByCurrentUser,
                    canDelete);
            })
            .ToList();
    }

    public static async Task<PostResponse> ToPostResponseAsync(
        IUnitOfWork unitOfWork,
        IUserResourceService userResourceService,
        Guid? requesterUserId,
        Post post,
        CancellationToken cancellationToken)
    {
        var responses = await ToPostResponsesAsync(unitOfWork, userResourceService, requesterUserId, new[] { post }, cancellationToken);
        return responses[0];
    }

    private static async Task<IReadOnlyDictionary<Guid, PostAuthorResponse>> LoadPostAuthorsByUserIdsAsync(
        IUserResourceService userResourceService,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var distinctUserIds = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctUserIds.Count == 0)
        {
            return new Dictionary<Guid, PostAuthorResponse>();
        }

        var profilesResult = await userResourceService.GetPublicUserProfilesByIdsAsync(distinctUserIds, cancellationToken);
        if (profilesResult.IsFailure)
        {
            return distinctUserIds.ToDictionary(id => id, CreateFallbackAuthor);
        }

        return distinctUserIds.ToDictionary(
            id => id,
            id => profilesResult.Value.TryGetValue(id, out var profile)
                ? new PostAuthorResponse(profile.UserId, profile.Username, profile.AvatarUrl)
                : CreateFallbackAuthor(id));
    }

    private static PostAuthorResponse CreateFallbackAuthor(Guid userId)
    {
        return new PostAuthorResponse(userId, UnknownUsername, null);
    }

    private static CommentAuthorResponse CreateFallbackCommentAuthor(Guid userId)
    {
        return new CommentAuthorResponse(userId, UnknownUsername, null);
    }

    [GeneratedRegex(@"(?<!\w)#[\p{L}\p{M}\p{N}_]+", RegexOptions.Compiled)]
    private static partial Regex HashtagRegexFactory();
}
