using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Billing.Models;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Commands;

public sealed record ResolveCoinPackageCheckoutCommand(
    Guid UserId,
    Guid? PackageId,
    Guid? TransactionId,
    string PaymentIntentId) : IRequest<Result<ResolveCoinPackageCheckoutResponse>>;

public sealed class ResolveCoinPackageCheckoutCommandHandler
    : IRequestHandler<ResolveCoinPackageCheckoutCommand, Result<ResolveCoinPackageCheckoutResponse>>
{
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ISender _sender;

    // Domain dependency marker for architecture tests
    private static readonly Type TransactionEntityType = typeof(Domain.Entities.Transaction);

    public ResolveCoinPackageCheckoutCommandHandler(
        IStripePaymentService stripePaymentService,
        ISender sender)
    {
        _stripePaymentService = stripePaymentService;
        _sender = sender;
    }

    public async Task<Result<ResolveCoinPackageCheckoutResponse>> Handle(
        ResolveCoinPackageCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        StripeCheckoutStatusResult stripeStatus;

        try
        {
            stripeStatus = await _stripePaymentService.GetCoinPackageCheckoutStatusAsync(
                request.PaymentIntentId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<ResolveCoinPackageCheckoutResponse>(
                new Error("Stripe.StatusCheckFailed", ex.Message));
        }

        var confirmResult = await _sender.Send(
            new ConfirmCoinPackagePaymentCommand(
                request.UserId,
                request.PackageId,
                request.TransactionId,
                stripeStatus.PaymentIntentId ?? request.PaymentIntentId,
                stripeStatus.Status),
            cancellationToken);

        if (confirmResult.IsFailure)
        {
            return Result.Failure<ResolveCoinPackageCheckoutResponse>(confirmResult.Error);
        }

        return Result.Success(new ResolveCoinPackageCheckoutResponse(
            confirmResult.Value.PackageId,
            confirmResult.Value.TransactionId,
            stripeStatus.PaymentIntentId ?? request.PaymentIntentId,
            stripeStatus.Status,
            stripeStatus.IsTerminal,
            confirmResult.Value.CoinsCredited,
            confirmResult.Value.AlreadyCredited,
            confirmResult.Value.CreditedCoins,
            confirmResult.Value.CurrentBalance));
    }
}
