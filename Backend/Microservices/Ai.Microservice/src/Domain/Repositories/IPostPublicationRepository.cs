using Domain.Entities;

namespace Domain.Repositories;

public interface IPostPublicationRepository
{
    Task AddRangeAsync(IEnumerable<PostPublication> entities, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetByPostIdsAsync(IReadOnlyList<Guid> postIds, CancellationToken cancellationToken);
    Task<PostPublication?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetByPostIdForUpdateAsync(Guid postId, CancellationToken cancellationToken);
    void Update(PostPublication entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
