namespace Domain.Repositories;

public sealed record ScheduledPostDispatchCandidate(
    Guid PostId,
    Guid UserId,
    IReadOnlyList<Guid> SocialMediaIds,
    bool? IsPrivate,
    Guid? PublishingScheduleId);
