using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetSubscriptionsQuery : IRequest<Result<List<Subscription>>>;

public sealed class GetSubscriptionsQueryHandler
    : IRequestHandler<GetSubscriptionsQuery, Result<List<Subscription>>>
{
    private readonly IRepository<Subscription> _repository;

    public GetSubscriptionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<List<Subscription>>> Handle(
        GetSubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _repository.GetAll()
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(subscriptions);
    }
}
