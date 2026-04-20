using Domain.Entities;

namespace Application.Comments.Models;

public sealed record CommentAuthorResponse(
    Guid UserId,
    string Username,
    string? AvatarUrl);

public sealed record CommentLikeResponse(
    Guid CommentId,
    int LikesCount,
    bool IsLikedByCurrentUser);

public sealed record CommentResponse(
    Guid Id,
    Guid PostId,
    Guid UserId,
    string Username,
    string? AvatarUrl,
    Guid? ParentCommentId,
    string Content,
    int LikesCount,
    int RepliesCount,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    bool? IsLikedByCurrentUser,
    bool? CanDelete);

internal static class CommentResponseMapping
{
    public static CommentResponse ToResponse(
        Comment comment,
        CommentAuthorResponse author,
        bool? isLikedByCurrentUser = null,
        bool? canDelete = null)
    {
        return new CommentResponse(
            comment.Id,
            comment.PostId,
            author.UserId,
            author.Username,
            author.AvatarUrl,
            comment.ParentCommentId,
            comment.Content,
            comment.LikesCount,
            comment.RepliesCount,
            comment.CreatedAt,
            comment.UpdatedAt,
            isLikedByCurrentUser,
            canDelete);
    }
}
