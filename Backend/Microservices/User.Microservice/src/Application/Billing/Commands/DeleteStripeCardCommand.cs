using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Billing.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Commands;

public sealed record DeleteStripeCardCommand(Guid UserId, string PaymentMethodId)
    : IRequest<Result<bool>>;

public sealed class DeleteStripeCardCommandHandler
    : IRequestHandler<DeleteStripeCardCommand, Result<bool>>
{
    private readonly IRepository<UserSubscription> _userSubscriptionRepository;
    private readonly IStripeCustomerResolver _stripeCustomerResolver;
    private readonly IStripePaymentService _stripePaymentService;

    public DeleteStripeCardCommandHandler(
        IUnitOfWork unitOfWork,
        IStripeCustomerResolver stripeCustomerResolver,
        IStripePaymentService stripePaymentService)
    {
        _userSubscriptionRepository = unitOfWork.Repository<UserSubscription>();
        _stripeCustomerResolver = stripeCustomerResolver;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<bool>> Handle(
        DeleteStripeCardCommand request,
        CancellationToken cancellationToken)
    {
        var customerResult = await _stripeCustomerResolver.ResolveAsync(
            request.UserId,
            createIfMissing: false,
            cancellationToken);

        if (customerResult.IsFailure)
        {
            return Result.Failure<bool>(customerResult.Error);
        }

        var stripeSubscriptionIds = await GetStripeSubscriptionIdsAsync(
            request.UserId,
            cancellationToken);

        try
        {
            await _stripePaymentService.DeleteCardAsync(
                customerResult.Value.StripeCustomerId,
                request.PaymentMethodId.Trim(),
                stripeSubscriptionIds,
                cancellationToken);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            return Result.Failure<bool>(
                new Error("Stripe.CardDeleteFailed", ex.Message));
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
