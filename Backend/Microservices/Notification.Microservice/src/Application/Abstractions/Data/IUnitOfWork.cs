using SharedLibrary.Common;

namespace Application.Abstractions.Data;

/// <summary>
/// Local unit of work abstraction for the User service boundary.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
