using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;

namespace Application.Subscriptions.Commands;

public sealed record PatchSubscriptionCommand(
    Guid Id,
    string? Name,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Subscription?>;

public sealed class PatchSubscriptionCommandHandler
    : IRequestHandler<PatchSubscriptionCommand, Subscription?>
{
    private readonly IRepository<Subscription> _repository;

    public PatchSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Subscription?> Handle(
        PatchSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return null;
        }

        var updated = false;

        if (request.Name != null)
        {
            subscription.Name = SubscriptionHelpers.NormalizeName(request.Name);
            updated = true;
        }

        if (request.MeAiCoin.HasValue)
        {
            subscription.MeAiCoin = request.MeAiCoin;
            updated = true;
        }

        if (request.Limits != null)
        {
            subscription.Limits ??= new SubscriptionLimits();
            updated |= SubscriptionHelpers.ApplyLimitsPatch(subscription.Limits, request.Limits);
        }

        if (updated)
        {
            subscription.UpdatedAt = DateTime.UtcNow;
        }

        return subscription;
    }
}
