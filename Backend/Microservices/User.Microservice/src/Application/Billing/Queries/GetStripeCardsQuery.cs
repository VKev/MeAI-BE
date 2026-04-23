using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Billing.Models;
using Application.Billing.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Queries;

public sealed record GetStripeCardsQuery(Guid UserId)
    : IRequest<Result<List<StripeCardResponse>>>;

public sealed class GetStripeCardsQueryHandler
    : IRequestHandler<GetStripeCardsQuery, Result<List<StripeCardResponse>>>
{
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripeCustomerResolver _stripeCustomerResolver;
    private readonly IStripePaymentService _stripePaymentService;

    public GetStripeCardsQueryHandler(
        IUnitOfWork unitOfWork,
        IStripeCustomerResolver stripeCustomerResolver,
        IStripePaymentService stripePaymentService)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripeCustomerResolver = stripeCustomerResolver;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<List<StripeCardResponse>>> Handle(
        GetStripeCardsQuery request,
        CancellationToken cancellationToken)
    {
        var customerResult = await _stripeCustomerResolver.ResolveAsync(
            request.UserId,
            createIfMissing: false,
            cancellationToken);

        if (customerResult.IsFailure)
        {
            if (string.Equals(
                    customerResult.Error.Code,
                    StripeCustomerResolver.CustomerMissingCode,
                    StringComparison.Ordinal))
            {
                return Result.Success(new List<StripeCardResponse>());
            }

            return Result.Failure<List<StripeCardResponse>>(customerResult.Error);
        }

        var stripeSubscriptionId = await GetLatestStripeSubscriptionIdAsync(
            request.UserId,
            cancellationToken);

        try
        {
            var cards = await _stripePaymentService.GetCustomerCardsAsync(
                customerResult.Value.StripeCustomerId,
                stripeSubscriptionId,
                cancellationToken);

            return Result.Success(cards.Select(ToResponse).ToList());
        }
        catch (Exception ex)
        {
            return Result.Failure<List<StripeCardResponse>>(
                new Error("Stripe.CardsListFailed", ex.Message));
        }
    }

    private async Task<string?> GetLatestStripeSubscriptionIdAsync(
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
            .OrderByDescending(item => item.ActiveDate ?? item.CreatedAt ?? item.UpdatedAt)
            .Select(item => item.StripeSubscriptionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    internal static StripeCardResponse ToResponse(StripeCardResult card)
    {
        return new StripeCardResponse(
            card.PaymentMethodId,
            card.Brand,
            card.Last4,
            card.ExpMonth,
            card.ExpYear,
            card.Funding,
            card.Country,
            card.CardholderName,
            card.IsDefault,
            card.IsExpired);
    }
}
