using Application.Abstractions.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Extensions;

namespace Application.Common;

internal static class FeedModerationSupport
{
    public const string PendingStatus = "Pending";
    public const string InReviewStatus = "InReview";
    public const string ResolvedStatus = "Resolved";
    public const string DismissedStatus = "Dismissed";

    public const string NoAction = "None";
    public const string DeleteTargetPostAction = "DeleteTargetPost";

    public static string? NormalizeUsername(string? username)
    {
        return string.IsNullOrWhiteSpace(username) ? null : username.Trim();
    }

    public static string? NormalizeTargetType(string? targetType)
    {
        var normalized = FeedPostSupport.NormalizeOptionalText(targetType);
        if (normalized is null)
        {
            return null;
        }

        if (string.Equals(normalized, "Post", StringComparison.OrdinalIgnoreCase))
        {
            return "Post";
        }

        if (string.Equals(normalized, "Comment", StringComparison.OrdinalIgnoreCase))
        {
            return "Comment";
        }

        return null;
    }

    public static string? NormalizeStatus(string? status)
    {
        var normalized = FeedPostSupport.NormalizeOptionalText(status);
        if (normalized is null)
        {
            return null;
        }

        return normalized.ToLowerInvariant() switch
        {
            "pending" => PendingStatus,
            "inreview" => InReviewStatus,
            "resolved" => ResolvedStatus,
            "dismissed" => DismissedStatus,
            _ => null
        };
    }

    public static string NormalizeAction(string? action)
    {
        var normalized = FeedPostSupport.NormalizeOptionalText(action);
        if (normalized is null)
        {
            return NoAction;
        }

        return normalized.ToLowerInvariant() switch
        {
            "none" => NoAction,
            "deletetargetpost" => DeleteTargetPostAction,
            _ => string.Empty
        };
    }

    public static bool CanTransition(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, nextStatus, StringComparison.Ordinal))
        {
            return true;
        }

        return currentStatus switch
        {
            PendingStatus => nextStatus is InReviewStatus or ResolvedStatus or DismissedStatus,
            InReviewStatus => nextStatus is ResolvedStatus or DismissedStatus,
            ResolvedStatus => false,
            DismissedStatus => false,
            _ => false
        };
    }

    public static async Task SoftDeletePostAsync(
        IUnitOfWork unitOfWork,
        Post post,
        CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        post.IsDeleted = true;
        post.DeletedAt = now;
        post.UpdatedAt = now;
        unitOfWork.Repository<Post>().Update(post);

        var comments = await unitOfWork.Repository<Comment>()
            .GetAll()
            .Where(item => item.PostId == post.Id && !item.IsDeleted && item.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var comment in comments)
        {
            comment.IsDeleted = true;
            comment.DeletedAt = now;
            comment.UpdatedAt = now;
            unitOfWork.Repository<Comment>().Update(comment);
        }

        await DecrementHashtagCountsAsync(unitOfWork, post.Id, cancellationToken);
    }

    public static async Task<int> SoftDeleteCommentThreadAsync(
        IUnitOfWork unitOfWork,
        Post post,
        Comment rootComment,
        CancellationToken cancellationToken)
    {
        var activeComments = await unitOfWork.Repository<Comment>()
            .GetAll()
            .Where(item => item.PostId == post.Id && !item.IsDeleted && item.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var commentsById = activeComments.ToDictionary(item => item.Id);
        if (!commentsById.TryGetValue(rootComment.Id, out var selectedComment))
        {
            return 0;
        }

        var childrenByParentId = activeComments
            .Where(item => item.ParentCommentId.HasValue)
            .GroupBy(item => item.ParentCommentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToList());

        var idsToDelete = new List<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(selectedComment.Id);

        while (stack.Count > 0)
        {
            var currentId = stack.Pop();
            if (!commentsById.ContainsKey(currentId) || idsToDelete.Contains(currentId))
            {
                continue;
            }

            idsToDelete.Add(currentId);
            if (!childrenByParentId.TryGetValue(currentId, out var childIds))
            {
                continue;
            }

            foreach (var childId in childIds)
            {
                stack.Push(childId);
            }
        }

        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var commentId in idsToDelete)
        {
            var comment = commentsById[commentId];
            comment.IsDeleted = true;
            comment.DeletedAt = now;
            comment.UpdatedAt = now;
            unitOfWork.Repository<Comment>().Update(comment);
        }

        if (selectedComment.ParentCommentId.HasValue && commentsById.TryGetValue(selectedComment.ParentCommentId.Value, out var parentComment))
        {
            parentComment.RepliesCount = Math.Max(0, parentComment.RepliesCount - 1);
            parentComment.UpdatedAt = now;
            unitOfWork.Repository<Comment>().Update(parentComment);
        }

        post.CommentsCount = Math.Max(0, post.CommentsCount - idsToDelete.Count);
        post.UpdatedAt = now;
        unitOfWork.Repository<Post>().Update(post);

        return idsToDelete.Count;
    }

    private static async Task DecrementHashtagCountsAsync(
        IUnitOfWork unitOfWork,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var postHashtags = await unitOfWork.Repository<PostHashtag>()
            .GetAll()
            .Where(item => item.PostId == postId)
            .ToListAsync(cancellationToken);

        if (postHashtags.Count == 0)
        {
            return;
        }

        var hashtagIds = postHashtags.Select(item => item.HashtagId).Distinct().ToList();
        var hashtags = await unitOfWork.Repository<Hashtag>()
            .GetAll()
            .Where(item => hashtagIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        foreach (var hashtag in hashtags)
        {
            hashtag.PostCount = Math.Max(0, hashtag.PostCount - postHashtags.Count(link => link.HashtagId == hashtag.Id));
            unitOfWork.Repository<Hashtag>().Update(hashtag);
        }
    }
}
