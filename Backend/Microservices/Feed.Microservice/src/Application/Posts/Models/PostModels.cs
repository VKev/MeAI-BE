using Application.Abstractions.Resources;
using Domain.Entities;

namespace Application.Posts.Models;

public sealed record PostMediaResponse(
    Guid ResourceId,
    string PresignedUrl,
    string ContentType,
    string ResourceType);

public sealed record PostResponse(
    Guid Id,
    Guid UserId,
    string? Content,
    string? MediaUrl,
    string? MediaType,
    IReadOnlyList<PostMediaResponse> Media,
    int LikesCount,
    int CommentsCount,
    int SharesCount,
    IReadOnlyList<string> Hashtags,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

internal static class PostResponseMapping
{
    public static PostResponse ToResponse(
        Post post,
        IReadOnlyList<string> hashtags,
        IReadOnlyList<UserResourcePresignResult> media)
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
            post.UserId,
            post.Content,
            primaryMedia?.PresignedUrl,
            post.MediaType ?? primaryMedia?.ResourceType ?? primaryMedia?.ContentType,
            mediaResponses,
            post.LikesCount,
            post.CommentsCount,
            post.SharesCount,
            hashtags,
            post.CreatedAt,
            post.UpdatedAt);
    }
}
