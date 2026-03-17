using Domain.Entities;

namespace Domain.Repositories;

public interface IPostPublicationRepository
{
    Task AddRangeAsync(IEnumerable<PostPublication> entities, CancellationToken cancellationToken);
    Task<IReadOnlyList<PostPublication>> GetByPostIdsAsync(IReadOnlyList<Guid> postIds, CancellationToken cancellationToken);
}
