using Application.Posts.Models;
using Domain.Entities;

namespace Application.Posts;

internal static class PostMapping
{
    public static PostResponse ToResponse(Post post)
    {
        return new PostResponse(
            Id: post.Id,
            UserId: post.UserId,
            SocialMediaId: post.SocialMediaId,
            Title: post.Title,
            Content: post.Content,
            Status: post.Status,
            CreatedAt: post.CreatedAt,
            UpdatedAt: post.UpdatedAt);
    }
}
