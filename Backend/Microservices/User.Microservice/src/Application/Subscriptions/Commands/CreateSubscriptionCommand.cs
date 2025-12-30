using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;

namespace Application.Subscriptions.Commands;

public sealed record CreateSubscriptionCommand(
    string? Name,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Subscription>;

public sealed class CreateSubscriptionCommandHandler
    : IRequestHandler<CreateSubscriptionCommand, Subscription>
{
    private readonly IRepository<Subscription> _repository;

    public CreateSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Subscription> Handle(
        CreateSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Name = SubscriptionHelpers.NormalizeName(request.Name),
            MeAiCoin = request.MeAiCoin,
            Limits = request.Limits,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(subscription, cancellationToken);

        return subscription;
    }
}
