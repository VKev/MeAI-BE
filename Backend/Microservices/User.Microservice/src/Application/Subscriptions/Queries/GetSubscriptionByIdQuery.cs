using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Queries;

public sealed record GetSubscriptionByIdQuery(Guid Id) : IRequest<Result<Subscription>>;

public sealed class GetSubscriptionByIdQueryHandler
    : IRequestHandler<GetSubscriptionByIdQuery, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;

    public GetSubscriptionByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<Subscription>> Handle(
        GetSubscriptionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var subscription = await _repository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (subscription == null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        return Result.Success(subscription);
    }
}
