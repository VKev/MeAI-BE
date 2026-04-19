using System.Security.Claims;
using Application.Subscriptions.Commands;
using Application.Subscriptions.Models;
using Application.Subscriptions.Queries;
using Application.Users.Models;
using AutoMapper;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/subscriptions")]
public sealed class SubscriptionsController : ApiController
{
    private readonly IMapper _mapper;

    public SubscriptionsController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<List<Subscription>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSubscriptionsQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("current")]
    [Authorize]
    [ProducesResponseType(typeof(Result<CurrentUserSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetCurrentUserSubscriptionQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(Result<List<CurrentUserSubscriptionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetMySubscriptionsQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{subscriptionId:guid}/purchase")]
    [Authorize]
    [ProducesResponseType(typeof(Result<PurchaseSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Purchase(
        Guid subscriptionId,
        [FromBody] PurchaseSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var command = _mapper.Map<PurchaseSubscriptionCommand>(request) with
        {
            SubscriptionId = subscriptionId,
            UserId = userId
        };

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{subscriptionId:guid}/purchase/confirm")]
    [Authorize]
    [ProducesResponseType(typeof(Result<ResolveStripeCheckoutResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPurchase(
        Guid subscriptionId,
        [FromBody] ConfirmStripePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new ResolveStripeCheckoutCommand(
                subscriptionId,
                userId,
                request.TransactionId,
                request.PaymentIntentId,
                request.StripeSubscriptionId,
                request.Renew),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}

public sealed record CreateSubscriptionRequest(
    string? Name,
    float? Cost,
    int DurationMonths,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record UpdateSubscriptionRequest(
    string? Name,
    float? Cost,
    int DurationMonths,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record PatchSubscriptionRequest(
    string? Name,
    float? Cost,
    int? DurationMonths,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record PurchaseSubscriptionRequest(string? PaymentMethodId, bool Renew);

public sealed record ConfirmStripePurchaseRequest(
    string? PaymentIntentId,
    string? StripeSubscriptionId,
    Guid? TransactionId,
    bool Renew);
