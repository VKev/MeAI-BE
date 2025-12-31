using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record DeleteSubscriptionCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteSubscriptionCommandHandler : IRequestHandler<DeleteSubscriptionCommand, Result<bool>>
{
    private readonly IRepository<Subscription> _repository;

    public DeleteSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<bool>> Handle(DeleteSubscriptionCommand request, CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return Result.Failure<bool>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        _repository.Delete(subscription);
        return Result.Success(true);
    }
}
