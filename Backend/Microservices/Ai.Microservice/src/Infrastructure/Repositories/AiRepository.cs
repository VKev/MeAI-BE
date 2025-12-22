using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Common;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class AiRepository :  Repository<Ai>, IAiRepository
    {
        public AiRepository(MyDbContext context) : base(context)
        {
        }

        public Task<Ai?> GetByEmailAsync(string email, CancellationToken cancellationToken)
        {
            return _context.Ais.FirstOrDefaultAsync(g => g.Email == email, cancellationToken);
        }
    }
}
