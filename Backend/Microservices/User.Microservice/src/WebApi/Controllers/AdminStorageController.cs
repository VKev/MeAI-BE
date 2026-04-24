using Application.Configs.Commands;
using Application.Configs.Models;
using Application.Configs.Queries;
using Application.Resources.Models;
using Application.Resources.Queries;
using Application.Subscriptions.Commands;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/admin/storage")]
[Authorize("ADMIN", "Admin")]
public sealed class AdminStorageController : ApiController
{
    public AdminStorageController(IMediator mediator)
        : base(mediator)
    {
    }

    [HttpGet("settings")]
    [ProducesResponseType(typeof(Result<ConfigResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConfigQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("settings/free-tier")]
    [ProducesResponseType(typeof(Result<ConfigResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateFreeTier(
        [FromBody] UpdateFreeStorageQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateFreeStorageQuotaCommand(request.FreeStorageQuotaBytes),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("plans")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<StoragePlanResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStoragePlansQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("plans/{subscriptionId:guid}")]
    [ProducesResponseType(typeof(Result<StoragePlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPlan(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStoragePlanByIdQuery(subscriptionId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPatch("plans/{subscriptionId:guid}")]
    [ProducesResponseType(typeof(Result<Subscription>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePlanStorage(
        Guid subscriptionId,
        [FromBody] UpdateStoragePlanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new PatchSubscriptionCommand(
                subscriptionId,
                null,
                null,
                null,
                null,
                new SubscriptionLimits
                {
                    StorageQuotaBytes = request.StorageQuotaBytes,
                    MaxUploadFileBytes = request.MaxUploadFileBytes,
                    RetentionDaysAfterDelete = request.RetentionDaysAfterDelete
                }),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("usage")]
    [ProducesResponseType(typeof(Result<AdminStorageUsageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsage(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? subscriptionId,
        [FromQuery] bool overQuotaOnly,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminStorageUsageQuery(userId, subscriptionId, overQuotaOnly),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record UpdateFreeStorageQuotaRequest(long FreeStorageQuotaBytes);

public sealed record UpdateStoragePlanRequest(
    long? StorageQuotaBytes,
    long? MaxUploadFileBytes,
    int? RetentionDaysAfterDelete);
