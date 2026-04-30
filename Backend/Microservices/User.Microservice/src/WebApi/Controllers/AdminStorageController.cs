using Application.Configs.Commands;
using Application.Resources.Commands;
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

    [HttpGet("settings/free-tier")]
    [ProducesResponseType(typeof(Result<StorageSettingsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFreeTierSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStorageSettingsQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("settings/free-tier")]
    [ProducesResponseType(typeof(Result<StorageSettingsResponse>), StatusCodes.Status200OK)]
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

    [HttpGet("settings/system")]
    [ProducesResponseType(typeof(Result<SystemStorageSettingsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSystemSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSystemStorageSettingsQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("settings/system")]
    [ProducesResponseType(typeof(Result<SystemStorageSettingsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSystemSettings(
        [FromBody] UpdateSystemStorageQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateSystemStorageQuotaCommand(request.SystemStorageQuotaBytes),
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
        [FromQuery] string? @namespace,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminStorageUsageQuery(userId, subscriptionId, overQuotaOnly, @namespace),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("usage/users/{userId:guid}")]
    [ProducesResponseType(typeof(Result<AdminStorageUsageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUserUsageDetail(
        Guid userId,
        [FromQuery] string? @namespace,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminStorageUsageQuery(userId, null, false, @namespace),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("resources")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<AdminStorageResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetResources(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? resourceType,
        [FromQuery] string[]? originKinds,
        [FromQuery] bool includeDeleted,
        [FromQuery] string? @namespace,
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        [FromQuery] bool includePresignedUrl,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminStorageResourcesQuery(
                userId,
                workspaceId,
                resourceType,
                originKinds,
                includeDeleted,
                @namespace,
                cursorCreatedAt,
                cursorId,
                limit,
                includePresignedUrl),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("cleanup/run")]
    [ProducesResponseType(typeof(Result<StorageCleanupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunCleanup(
        [FromBody] RunStorageCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RunStorageCleanupCommand(
                request.DryRun ?? true,
                request.DeleteExpiredResources ?? true,
                request.DeleteOrphanObjects ?? false,
                request.OlderThanDays ?? 30,
                request.Namespace),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("reconcile")]
    [ProducesResponseType(typeof(Result<StorageReconcileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reconcile(
        [FromBody] ReconcileStorageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ReconcileStorageCommand(
                request.DryRun ?? true,
                request.MarkMissingObjects ?? false,
                request.Namespace),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record UpdateFreeStorageQuotaRequest(long FreeStorageQuotaBytes);

public sealed record UpdateSystemStorageQuotaRequest(long? SystemStorageQuotaBytes);

public sealed record UpdateStoragePlanRequest(
    long? StorageQuotaBytes,
    long? MaxUploadFileBytes,
    int? RetentionDaysAfterDelete);

public sealed record RunStorageCleanupRequest(
    bool? DryRun,
    bool? DeleteExpiredResources,
    bool? DeleteOrphanObjects,
    int? OlderThanDays,
    string? Namespace);

public sealed record ReconcileStorageRequest(
    bool? DryRun,
    bool? MarkMissingObjects,
    string? Namespace);
