using Application.Subscriptions.Commands;
using Application.Subscriptions.Queries;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/subscriptions")]
[Authorize("Admin")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly ISender _sender;

    public SubscriptionsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<Subscription>>> GetAll(CancellationToken cancellationToken)
    {
        var subscriptions = await _sender.Send(new GetSubscriptionsQuery(), cancellationToken);
        return Ok(subscriptions);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Subscription>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _sender.Send(new GetSubscriptionByIdQuery(id), cancellationToken);

        if (subscription == null)
        {
            return NotFound(new { message = "Subscription not found." });
        }

        return Ok(subscription);
    }

    [HttpPost]
    public async Task<ActionResult<Subscription>> Create(
        [FromBody] CreateSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await _sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = subscription.Id }, subscription);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Subscription>> Update(
        Guid id,
        [FromBody] UpdateSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await _sender.Send(command with { Id = id }, cancellationToken);

        if (subscription == null)
        {
            return NotFound(new { message = "Subscription not found." });
        }

        return Ok(subscription);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<Subscription>> Patch(
        Guid id,
        [FromBody] PatchSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await _sender.Send(command with { Id = id }, cancellationToken);

        if (subscription == null)
        {
            return NotFound(new { message = "Subscription not found." });
        }

        return Ok(subscription);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _sender.Send(new DeleteSubscriptionCommand(id), cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = "Subscription not found." });
        }

        return NoContent();
    }
}
