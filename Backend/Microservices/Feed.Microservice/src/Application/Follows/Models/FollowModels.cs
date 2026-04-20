namespace Application.Follows.Models;

public sealed record FollowUserResponse(
    Guid UserId,
    DateTime? FollowedAt);

public sealed record FollowSuggestionResponse(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl,
    int PostCount);
