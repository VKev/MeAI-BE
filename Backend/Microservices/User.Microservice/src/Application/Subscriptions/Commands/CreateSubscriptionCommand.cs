using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record CreateSubscriptionCommand(
    string? Name,
    float? Cost,
    int DurationMonths,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Result<Subscription>>;

public sealed class CreateSubscriptionCommandHandler
    : IRequestHandler<CreateSubscriptionCommand, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;

    public CreateSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<Subscription>> Handle(
        CreateSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Name = SubscriptionHelpers.NormalizeName(request.Name),
            Cost = request.Cost,
            DurationMonths = request.DurationMonths,
            MeAiCoin = request.MeAiCoin,
            Limits = request.Limits,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(subscription, cancellationToken);

        return Result.Success(subscription);
    }
}
