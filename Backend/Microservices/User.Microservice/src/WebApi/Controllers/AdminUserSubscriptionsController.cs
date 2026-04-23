using Application.Subscriptions.Commands;
using Application.Subscriptions.Models;
using Application.Subscriptions.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/admin/user-subscriptions")]
[Authorize("ADMIN", "Admin")]
public sealed class AdminUserSubscriptionsController : ApiController
{
    public AdminUserSubscriptionsController(IMediator mediator)
        : base(mediator)
    {
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<AdminUserSubscriptionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? userId,
        [FromQuery] string? status,
        [FromQuery] bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminUserSubscriptionsQuery(userId, status, includeDeleted),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{userSubscriptionId:guid}")]
    [ProducesResponseType(typeof(Result<AdminUserSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(
        Guid userSubscriptionId,
        [FromQuery] bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminUserSubscriptionByIdQuery(userSubscriptionId, includeDeleted),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("{userSubscriptionId:guid}/status")]
    [ProducesResponseType(typeof(Result<AdminUserSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatus(
        Guid userSubscriptionId,
        [FromBody] UpdateUserSubscriptionStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateUserSubscriptionStatusCommand(userSubscriptionId, request.Status),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record UpdateUserSubscriptionStatusRequest(string Status);
