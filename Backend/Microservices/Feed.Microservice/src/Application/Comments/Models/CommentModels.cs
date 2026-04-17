using Domain.Entities;

namespace Application.Comments.Models;

public sealed record CommentResponse(
    Guid Id,
    Guid PostId,
    Guid UserId,
    Guid? ParentCommentId,
    string Content,
    int LikesCount,
    int RepliesCount,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

internal static class CommentResponseMapping
{
    public static CommentResponse ToResponse(Comment comment)
    {
        return new CommentResponse(
            comment.Id,
            comment.PostId,
            comment.UserId,
            comment.ParentCommentId,
            comment.Content,
            comment.LikesCount,
            comment.RepliesCount,
            comment.CreatedAt,
            comment.UpdatedAt);
    }
}
