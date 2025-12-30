using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;

namespace Application.Subscriptions.Commands;

public sealed record DeleteSubscriptionCommand(Guid Id) : IRequest<bool>;

public sealed class DeleteSubscriptionCommandHandler : IRequestHandler<DeleteSubscriptionCommand, bool>
{
    private readonly IRepository<Subscription> _repository;

    public DeleteSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<bool> Handle(DeleteSubscriptionCommand request, CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return false;
        }

        _repository.Delete(subscription);
        return true;
    }
}
