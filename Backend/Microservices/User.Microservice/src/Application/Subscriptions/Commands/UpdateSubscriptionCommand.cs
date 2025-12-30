using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;

namespace Application.Subscriptions.Commands;

public sealed record UpdateSubscriptionCommand(
    Guid Id,
    string? Name,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Subscription?>;

public sealed class UpdateSubscriptionCommandHandler
    : IRequestHandler<UpdateSubscriptionCommand, Subscription?>
{
    private readonly IRepository<Subscription> _repository;

    public UpdateSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Subscription?> Handle(
        UpdateSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return null;
        }

        subscription.Name = SubscriptionHelpers.NormalizeName(request.Name);
        subscription.MeAiCoin = request.MeAiCoin;
        subscription.Limits = request.Limits;
        subscription.UpdatedAt = DateTime.UtcNow;

        return subscription;
    }
}
