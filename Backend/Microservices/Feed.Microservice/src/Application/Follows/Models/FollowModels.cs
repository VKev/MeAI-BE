namespace Application.Follows.Models;

public sealed record FollowUserResponse(
    Guid FollowId,
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl,
    int PostCount,
    DateTime? FollowedAt);

public sealed record FollowSuggestionResponse(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl,
    int PostCount);
