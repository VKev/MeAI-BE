namespace Application.Follows.Models;

public sealed record FollowUserResponse(
    Guid UserId,
    DateTime? FollowedAt);
