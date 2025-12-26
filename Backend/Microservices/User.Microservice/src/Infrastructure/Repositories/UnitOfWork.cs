using Application.Abstractions.Data;
using Infrastructure.Persistence.Context;

namespace Infrastructure.Repositories;

public sealed class UnitOfWork(MyDbContext context) : IUnitOfWork, IDisposable
{
    public void Dispose()
    {
        context.Dispose();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return context.SaveChangesAsync(cancellationToken);
    }
}