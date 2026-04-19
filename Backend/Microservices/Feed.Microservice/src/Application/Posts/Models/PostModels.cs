using Application.Abstractions.Resources;
using Domain.Entities;

namespace Application.Posts.Models;

public sealed record PostMediaResponse(
    Guid ResourceId,
    string PresignedUrl,
    string ContentType,
    string ResourceType);

public sealed record PostAuthorResponse(
    Guid UserId,
    string Username,
    string? AvatarUrl);

public sealed record PostResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string? AvatarUrl,
    string? Content,
    string? MediaUrl,
    string? MediaType,
    IReadOnlyList<PostMediaResponse> Media,
    int LikesCount,
    int CommentsCount,
    IReadOnlyList<string> Hashtags,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    bool? IsLikedByCurrentUser,
    bool? CanDelete);

internal static class PostResponseMapping
{
    public static PostResponse ToResponse(
        Post post,
        PostAuthorResponse author,
        IReadOnlyList<string> hashtags,
        IReadOnlyList<UserResourcePresignResult> media,
        bool? isLikedByCurrentUser = null,
        bool? canDelete = null)
    {
        var mediaResponses = media
            .Select(item => new PostMediaResponse(
                item.ResourceId,
                item.PresignedUrl,
                item.ContentType,
                item.ResourceType))
            .ToList();

        var primaryMedia = mediaResponses.FirstOrDefault();

        return new PostResponse(
            post.Id,
            author.UserId,
            author.Username,
            author.AvatarUrl,
            post.Content,
            primaryMedia?.PresignedUrl,
            post.MediaType ?? primaryMedia?.ResourceType ?? primaryMedia?.ContentType,
            mediaResponses,
            post.LikesCount,
            post.CommentsCount,
            hashtags,
            post.CreatedAt,
            post.UpdatedAt,
            isLikedByCurrentUser,
            canDelete);
    }
}
