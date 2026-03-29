using Application.Abstractions.Payments;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Subscriptions.Commands;

public sealed record ResolveStripeCheckoutCommand(
    Guid SubscriptionId,
    Guid UserId,
    Guid? TransactionId,
    string? PaymentIntentId,
    string? StripeSubscriptionId,
    bool Renew) : IRequest<Result<ResolveStripeCheckoutResponse>>;

public sealed record ResolveStripeCheckoutResponse(
    string Status,
    bool IsFinal,
    bool SubscriptionActivated);

public sealed class ResolveStripeCheckoutCommandHandler
    : IRequestHandler<ResolveStripeCheckoutCommand, Result<ResolveStripeCheckoutResponse>>
{
    private readonly IStripePaymentService _stripePaymentService;
    private readonly ISender _sender;

    public ResolveStripeCheckoutCommandHandler(
        IStripePaymentService stripePaymentService,
        ISender sender)
    {
        _stripePaymentService = stripePaymentService;
        _sender = sender;
    }

    public async Task<Result<ResolveStripeCheckoutResponse>> Handle(
        ResolveStripeCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentIntentId) &&
            string.IsNullOrWhiteSpace(request.StripeSubscriptionId))
        {
            return Result.Failure<ResolveStripeCheckoutResponse>(
                new Error("Stripe.MissingIdentifiers", "Stripe payment identifiers are missing."));
        }

        StripeCheckoutStatusResult stripeStatus;

        try
        {
            stripeStatus = await _stripePaymentService.GetCheckoutStatusAsync(
                request.PaymentIntentId,
                request.StripeSubscriptionId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<ResolveStripeCheckoutResponse>(
                new Error("Stripe.StatusCheckFailed", ex.Message));
        }

        var confirmResult = await _sender.Send(
            new ConfirmSubscriptionPaymentCommand(
                request.UserId,
                request.SubscriptionId,
                request.TransactionId,
                request.Renew,
                stripeStatus.Status),
            cancellationToken);

        if (confirmResult.IsFailure)
        {
            return Result.Failure<ResolveStripeCheckoutResponse>(confirmResult.Error);
        }

        return Result.Success(new ResolveStripeCheckoutResponse(
            stripeStatus.Status,
            stripeStatus.IsTerminal,
            stripeStatus.IsSuccessful));
    }
}
