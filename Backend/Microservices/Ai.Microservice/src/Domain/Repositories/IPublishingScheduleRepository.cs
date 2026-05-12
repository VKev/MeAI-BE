using Domain.Entities;

namespace Domain.Repositories;

public interface IPublishingScheduleRepository
{
    Task AddAsync(PublishingSchedule entity, CancellationToken cancellationToken);
    void Update(PublishingSchedule entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<PublishingSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<PublishingSchedule?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ClaimDueAgenticSchedulesAsync(
        DateTime dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PublishingSchedule>> GetByUserIdAsync(
        Guid userId,
        Guid? workspaceId,
        string? status,
        int limit,
        CancellationToken cancellationToken);
}
