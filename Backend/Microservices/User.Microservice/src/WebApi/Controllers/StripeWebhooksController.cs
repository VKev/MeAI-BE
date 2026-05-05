using Application.Billing.Commands;
using Application.Subscriptions.Commands;
using Application.Abstractions.ApiCredentials;
using Infrastructure.Configs;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using Stripe;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/webhooks/stripe")]
public sealed class StripeWebhooksController : ApiController
{
    private readonly IApiCredentialProvider _credentialProvider;

    public StripeWebhooksController(
        IApiCredentialProvider credentialProvider,
        IMediator mediator)
        : base(mediator)
    {
        _credentialProvider = credentialProvider;
    }

    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Handle(CancellationToken cancellationToken)
    {
        var webhookSecret = _credentialProvider.GetOptionalValue("Stripe", "WebhookSecret");
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Stripe.WebhookSecretMissing", "Stripe webhook secret is not configured.")));
        }

        var json = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        Event stripeEvent;
        try
        {
                stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                webhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (Exception ex)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Stripe.WebhookInvalidSignature", ex.Message)));
        }

        switch (stripeEvent.Type)
        {
            case Events.PaymentIntentSucceeded:
            {
                if (stripeEvent.Data.Object is PaymentIntent paymentIntent)
                {
                    var result = await HandlePaymentSuccessAsync(
                        paymentIntent.Metadata,
                        paymentIntent.Id,
                        null,
                        paymentIntent.Status,
                        cancellationToken);
                    if (result.IsFailure)
                    {
                        return HandleFailure(result);
                    }

                    return Ok(result);
                }
                break;
            }
            case Events.InvoicePaymentSucceeded:
            case "invoice.paid":
            {
                if (stripeEvent.Data.Object is Invoice invoice)
                {
                    var metadata = new Dictionary<string, string>(invoice.Metadata ?? new Dictionary<string, string>());
                    if (RequiresMetadata(metadata) &&
                        !string.IsNullOrWhiteSpace(invoice.SubscriptionId) &&
                        !string.IsNullOrWhiteSpace(_credentialProvider.GetOptionalValue("Stripe", "SecretKey")))
                    {
                        var subscription = await CreateSubscriptionService().GetAsync(
                            invoice.SubscriptionId,
                            cancellationToken: cancellationToken);

                        if (subscription?.Metadata != null)
                        {
                            foreach (var entry in subscription.Metadata)
                            {
                                if (!metadata.ContainsKey(entry.Key))
                                {
                                    metadata[entry.Key] = entry.Value;
                                }
                            }
                        }
                    }

                    var result = await HandlePaymentSuccessAsync(
                        metadata,
                        invoice.Id,
                        invoice.SubscriptionId,
                        invoice.Status,
                        cancellationToken);
                    if (result.IsFailure)
                    {
                        return HandleFailure(result);
                    }

                    return Ok(result);
                }
                break;
            }
        }

        return Ok(Result.Success(true));
    }

    private async Task<Result<bool>> HandlePaymentSuccessAsync(
        IDictionary<string, string> metadata,
        string? providerReferenceId,
        string? stripeSubscriptionId,
        string? status,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "succeeded" : status.Trim();

        if (TryParseCoinPackageMetadata(metadata, out var coinPackageUserId, out var packageId, out var coinPackageTransactionId))
        {
            if (string.IsNullOrWhiteSpace(providerReferenceId))
            {
                return Result.Failure<bool>(
                    new Error("Stripe.WebhookPaymentIntentMissing", "Stripe payment intent id is missing for the coin package webhook."));
            }

            var confirmCoinPackageResult = await _mediator.Send(
                new ConfirmCoinPackagePaymentCommand(
                    coinPackageUserId,
                    packageId,
                    coinPackageTransactionId,
                    providerReferenceId,
                    normalizedStatus),
                cancellationToken);

            if (confirmCoinPackageResult.IsFailure)
            {
                return Result.Failure<bool>(confirmCoinPackageResult.Error);
            }

            return Result.Success(true);
        }

        if (!TryParseMetadata(metadata, out var userId, out var subscriptionId, out var transactionId, out var renew))
        {
            return Result.Failure<bool>(
                new Error("Stripe.WebhookMetadataMissing", "Webhook metadata is missing user_id or subscription_id."));
        }

        var command = new ConfirmSubscriptionPaymentCommand(
            userId,
            subscriptionId,
            transactionId,
            providerReferenceId,
            stripeSubscriptionId,
            renew,
            normalizedStatus);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.Error);
        }

        return Result.Success(true);
    }

    private static bool RequiresMetadata(IDictionary<string, string> metadata)
    {
        return !metadata.ContainsKey("user_id") || !metadata.ContainsKey("subscription_id");
    }

    private static bool TryParseCoinPackageMetadata(
        IDictionary<string, string> metadata,
        out Guid userId,
        out Guid? packageId,
        out Guid? transactionId)
    {
        userId = Guid.Empty;
        packageId = null;
        transactionId = null;

        if (!metadata.TryGetValue("flow_type", out var flowType) ||
            !string.Equals(flowType, "coin_package", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!metadata.TryGetValue("user_id", out var userIdValue) ||
            !Guid.TryParse(userIdValue, out userId))
        {
            return false;
        }

        if (metadata.TryGetValue("coin_package_id", out var packageIdValue) &&
            Guid.TryParse(packageIdValue, out var parsedPackageId))
        {
            packageId = parsedPackageId;
        }

        if (metadata.TryGetValue("transaction_id", out var transactionIdValue) &&
            Guid.TryParse(transactionIdValue, out var parsedTransactionId))
        {
            transactionId = parsedTransactionId;
        }

        return true;
    }

    private SubscriptionService CreateSubscriptionService()
    {
        var secretKey = _credentialProvider.GetRequiredValue("Stripe", "SecretKey");
        return new SubscriptionService(new StripeClient(secretKey));
    }

    private static bool TryParseMetadata(
        IDictionary<string, string> metadata,
        out Guid userId,
        out Guid subscriptionId,
        out Guid? transactionId,
        out bool renew)
    {
        renew = false;
        userId = Guid.Empty;
        subscriptionId = Guid.Empty;
        transactionId = null;

        if (!metadata.TryGetValue("user_id", out var userIdValue) ||
            !Guid.TryParse(userIdValue, out userId))
        {
            return false;
        }

        if (!metadata.TryGetValue("subscription_id", out var subscriptionIdValue) ||
            !Guid.TryParse(subscriptionIdValue, out subscriptionId))
        {
            return false;
        }

        if (metadata.TryGetValue("transaction_id", out var transactionIdValue) &&
            Guid.TryParse(transactionIdValue, out var parsedTransactionId))
        {
            transactionId = parsedTransactionId;
        }

        if (metadata.TryGetValue("renew", out var renewValue))
        {
            _ = bool.TryParse(renewValue, out renew);
        }

        return true;
    }
}
