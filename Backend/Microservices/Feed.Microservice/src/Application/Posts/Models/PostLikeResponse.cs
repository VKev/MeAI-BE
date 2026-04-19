namespace Application.Posts.Models;

public sealed record PostLikeResponse(
    Guid PostId,
    int LikesCount,
    bool IsLikedByCurrentUser);
