using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record PatchSubscriptionCommand(
    Guid Id,
    string? Name,
    float? Cost,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Result<Subscription>>;

public sealed class PatchSubscriptionCommandHandler
    : IRequestHandler<PatchSubscriptionCommand, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;

    public PatchSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<Subscription>> Handle(
        PatchSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        var updated = false;

        if (request.Name != null)
        {
            subscription.Name = SubscriptionHelpers.NormalizeName(request.Name);
            updated = true;
        }

        if (request.Cost.HasValue)
        {
            subscription.Cost = request.Cost;
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

        return Result.Success(subscription);
    }
}
