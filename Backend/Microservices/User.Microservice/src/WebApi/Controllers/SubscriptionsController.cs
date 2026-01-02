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

    [HttpGet("{id:guid}")]
    [Authorize("Admin")]
    [ProducesResponseType(typeof(Result<Subscription>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSubscriptionByIdQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [Authorize("Admin")]
    [ProducesResponseType(typeof(Result<Subscription>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateSubscriptionCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize("Admin")]
    [ProducesResponseType(typeof(Result<Subscription>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<UpdateSubscriptionCommand>(request) with { Id = id };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPatch("{id:guid}")]
    [Authorize("Admin")]
    [ProducesResponseType(typeof(Result<Subscription>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] PatchSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<PatchSubscriptionCommand>(request) with { Id = id };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize("Admin")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteSubscriptionCommand(id), cancellationToken);
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

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}

public sealed record CreateSubscriptionRequest(
    string? Name,
    float? Cost,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record UpdateSubscriptionRequest(
    string? Name,
    float? Cost,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record PatchSubscriptionRequest(
    string? Name,
    float? Cost,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record PurchaseSubscriptionRequest(string? PaymentMethodId);
