using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Domain.Entities;

namespace Domain.Repositories;

public interface IAiRepository : IRepository<Ai>
{
    Task<Ai?> GetByEmailAsync(string email, CancellationToken cancellationToken);
}
