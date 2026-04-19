using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetAllSubscriptionsQuery : IRequest<Result<List<Subscription>>>;

public sealed class GetAllSubscriptionsQueryHandler
    : IRequestHandler<GetAllSubscriptionsQuery, Result<List<Subscription>>>
{
    private readonly IRepository<Subscription> _repository;

    public GetAllSubscriptionsQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<List<Subscription>>> Handle(
        GetAllSubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _repository.GetAll()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(subscriptions);
    }
}
