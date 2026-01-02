using Application.Subscriptions.Commands;
using Infrastructure.Configs;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using Stripe;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/webhooks/stripe")]
public sealed class StripeWebhooksController : ApiController
{
    private readonly StripeOptions _stripeOptions;
    private readonly SubscriptionService _subscriptionService;

    public StripeWebhooksController(
        IOptions<StripeOptions> stripeOptions,
        IMediator mediator)
        : base(mediator)
    {
        _stripeOptions = stripeOptions.Value;

        if (!string.IsNullOrWhiteSpace(_stripeOptions.SecretKey))
        {
            var client = new StripeClient(_stripeOptions.SecretKey);
            _subscriptionService = new SubscriptionService(client);
        }
        else
        {
            _subscriptionService = new SubscriptionService();
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Handle(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_stripeOptions.WebhookSecret))
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Stripe.WebhookSecretMissing", "Stripe webhook secret is not configured.")));
        }

        var json = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, _stripeOptions.WebhookSecret);
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
            {
                if (stripeEvent.Data.Object is Invoice invoice)
                {
                    var metadata = new Dictionary<string, string>(invoice.Metadata ?? new Dictionary<string, string>());
                    if (RequiresMetadata(metadata) &&
                        !string.IsNullOrWhiteSpace(invoice.SubscriptionId) &&
                        !string.IsNullOrWhiteSpace(_stripeOptions.SecretKey))
                    {
                        var subscription = await _subscriptionService.GetAsync(
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

                    var result = await HandlePaymentSuccessAsync(metadata, invoice.Status, cancellationToken);
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
        string? status,
        CancellationToken cancellationToken)
    {
        if (!TryParseMetadata(metadata, out var userId, out var subscriptionId, out var renew))
        {
            return Result.Failure<bool>(
                new Error("Stripe.WebhookMetadataMissing", "Webhook metadata is missing user_id or subscription_id."));
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "succeeded" : status.Trim();
        var command = new ConfirmSubscriptionPaymentCommand(userId, subscriptionId, renew, normalizedStatus);
        return await _mediator.Send(command, cancellationToken);
    }

    private static bool RequiresMetadata(IDictionary<string, string> metadata)
    {
        return !metadata.ContainsKey("user_id") || !metadata.ContainsKey("subscription_id");
    }

    private static bool TryParseMetadata(
        IDictionary<string, string> metadata,
        out Guid userId,
        out Guid subscriptionId,
        out bool renew)
    {
        renew = false;
        userId = Guid.Empty;
        subscriptionId = Guid.Empty;

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

        if (metadata.TryGetValue("renew", out var renewValue))
        {
            _ = bool.TryParse(renewValue, out renew);
        }

        return true;
    }
}
