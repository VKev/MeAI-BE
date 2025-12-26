using Domain.Entities;

namespace Domain.Repositories;

public interface ISocialMediaRepository
{
    Task<IReadOnlyList<SocialMedia>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<SocialMedia?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(SocialMedia socialMedia, CancellationToken cancellationToken = default);
    void Update(SocialMedia socialMedia);
}
