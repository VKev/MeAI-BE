using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Billing.Models;
using Application.Billing.Queries;
using Application.Billing.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Commands;

public sealed record SetDefaultStripeCardCommand(Guid UserId, string PaymentMethodId)
    : IRequest<Result<StripeCardResponse>>;

public sealed class SetDefaultStripeCardCommandHandler
    : IRequestHandler<SetDefaultStripeCardCommand, Result<StripeCardResponse>>
{
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripeCustomerResolver _stripeCustomerResolver;
    private readonly IStripePaymentService _stripePaymentService;

    public SetDefaultStripeCardCommandHandler(
        IUnitOfWork unitOfWork,
        IStripeCustomerResolver stripeCustomerResolver,
        IStripePaymentService stripePaymentService)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripeCustomerResolver = stripeCustomerResolver;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<StripeCardResponse>> Handle(
        SetDefaultStripeCardCommand request,
        CancellationToken cancellationToken)
    {
        var customerResult = await _stripeCustomerResolver.ResolveAsync(
            request.UserId,
            createIfMissing: false,
            cancellationToken);

        if (customerResult.IsFailure)
        {
            return Result.Failure<StripeCardResponse>(customerResult.Error);
        }

        var stripeSubscriptionIds = await GetStripeSubscriptionIdsAsync(
            request.UserId,
            cancellationToken);

        try
        {
            var card = await _stripePaymentService.SetDefaultCardAsync(
                customerResult.Value.StripeCustomerId,
                request.PaymentMethodId.Trim(),
                stripeSubscriptionIds,
                cancellationToken);

            return Result.Success(GetStripeCardsQueryHandler.ToResponse(card));
        }
        catch (Exception ex)
        {
            return Result.Failure<StripeCardResponse>(
                new Error("Stripe.CardSwitchFailed", ex.Message));
        }
    }

    private async Task<List<string>> GetStripeSubscriptionIdsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _userSubscriptionRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                !item.IsDeleted &&
                item.StripeSubscriptionId != null &&
                item.StripeSubscriptionId != string.Empty)
            .Select(item => item.StripeSubscriptionId!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
