using Application.Abstractions.Payments;
using Application.Billing.Models;
using Application.Billing.Services;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Commands;

public sealed record CreateStripeCardSetupIntentCommand(Guid UserId)
    : IRequest<Result<StripeCardSetupIntentResponse>>;

public sealed class CreateStripeCardSetupIntentCommandHandler
    : IRequestHandler<CreateStripeCardSetupIntentCommand, Result<StripeCardSetupIntentResponse>>
{
    private readonly IStripeCustomerResolver _stripeCustomerResolver;
    private readonly IStripePaymentService _stripePaymentService;

    public CreateStripeCardSetupIntentCommandHandler(
        IStripeCustomerResolver stripeCustomerResolver,
        IStripePaymentService stripePaymentService)
    {
        _stripeCustomerResolver = stripeCustomerResolver;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<Result<StripeCardSetupIntentResponse>> Handle(
        CreateStripeCardSetupIntentCommand request,
        CancellationToken cancellationToken)
    {
        var customerResult = await _stripeCustomerResolver.ResolveAsync(
            request.UserId,
            createIfMissing: true,
            cancellationToken);

        if (customerResult.IsFailure)
        {
            return Result.Failure<StripeCardSetupIntentResponse>(customerResult.Error);
        }

        try
        {
            var setupIntent = await _stripePaymentService.CreateCardSetupIntentAsync(
                customerResult.Value.StripeCustomerId,
                new Dictionary<string, string>
                {
                    ["user_id"] = request.UserId.ToString()
                },
                cancellationToken);

            return Result.Success(new StripeCardSetupIntentResponse(
                setupIntent.SetupIntentId,
                setupIntent.ClientSecret,
                setupIntent.StripeCustomerId));
        }
        catch (Exception ex)
        {
            return Result.Failure<StripeCardSetupIntentResponse>(
                new Error("Stripe.CardSetupFailed", ex.Message));
        }
    }
}
