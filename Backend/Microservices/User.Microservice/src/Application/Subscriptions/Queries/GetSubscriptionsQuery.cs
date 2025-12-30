using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;

namespace Application.Subscriptions.Queries;

public sealed record GetSubscriptionsQuery : IRequest<IReadOnlyList<Subscription>>;

public sealed class GetSubscriptionsQueryHandler
    : IRequestHandler<GetSubscriptionsQuery, IReadOnlyList<Subscription>>
{
    private readonly IRepository<Subscription> _repository;

    public GetSubscriptionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<IReadOnlyList<Subscription>> Handle(
        GetSubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAll()
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }
}
