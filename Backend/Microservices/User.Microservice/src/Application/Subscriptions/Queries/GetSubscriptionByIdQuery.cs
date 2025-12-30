using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;

namespace Application.Subscriptions.Queries;

public sealed record GetSubscriptionByIdQuery(Guid Id) : IRequest<Subscription?>;

public sealed class GetSubscriptionByIdQueryHandler
    : IRequestHandler<GetSubscriptionByIdQuery, Subscription?>
{
    private readonly IRepository<Subscription> _repository;

    public GetSubscriptionByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Subscription?> Handle(
        GetSubscriptionByIdQuery request,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
    }
}
