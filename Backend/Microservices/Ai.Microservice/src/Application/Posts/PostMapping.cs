using Application.Posts.Models;
using Domain.Entities;

namespace Application.Posts;

internal static class PostMapping
{
    private const string UnknownUsername = "unknown";

    public static PostResponse ToResponse(Post post)
    {
        return new PostResponse(
            Id: post.Id,
            UserId: post.UserId,
            Username: UnknownUsername,
            AvatarUrl: null,
            WorkspaceId: post.WorkspaceId,
            SocialMediaId: post.SocialMediaId,
            Title: post.Title,
            Content: post.Content,
            Status: post.Status,
            IsPublished: false,
            Media: Array.Empty<PostMediaResponse>(),
            Publications: Array.Empty<PostPublicationResponse>(),
            CreatedAt: post.CreatedAt,
            UpdatedAt: post.UpdatedAt);
    }
}
