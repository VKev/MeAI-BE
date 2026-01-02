using Application.Abstractions.Data;
using Application.Subscriptions.Helpers;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record UpdateSubscriptionCommand(
    Guid Id,
    string? Name,
    float? Cost,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits) : IRequest<Result<Subscription>>;

public sealed class UpdateSubscriptionCommandHandler
    : IRequestHandler<UpdateSubscriptionCommand, Result<Subscription>>
{
    private readonly IRepository<Subscription> _repository;

    public UpdateSubscriptionCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Subscription>();
    }

    public async Task<Result<Subscription>> Handle(
        UpdateSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        Subscription? subscription = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (subscription == null)
        {
            return Result.Failure<Subscription>(
                new Error("Subscription.NotFound", "Subscription not found."));
        }

        subscription.Name = SubscriptionHelpers.NormalizeName(request.Name);
        subscription.Cost = request.Cost;
        subscription.MeAiCoin = request.MeAiCoin;
        subscription.Limits = request.Limits;
        subscription.UpdatedAt = DateTime.UtcNow;

        return Result.Success(subscription);
    }
}
