using System.Threading;
using System.Threading.Tasks;

namespace SharedLibrary.Abstractions.UnitOfWork
{
    public interface ISaveChangesUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}


