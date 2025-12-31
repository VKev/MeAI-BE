using Application.Subscriptions.Commands;
using Application.Subscriptions.Queries;
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
[Authorize("Admin")]
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
}

public sealed record CreateSubscriptionRequest(
    string? Name,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record UpdateSubscriptionRequest(
    string? Name,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);

public sealed record PatchSubscriptionRequest(
    string? Name,
    decimal? MeAiCoin,
    SubscriptionLimits? Limits);
