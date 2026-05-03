using Application.Billing.Commands;
using Application.Billing.Models;
using Application.Billing.Queries;
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
[Route("api/User/admin/billing/coin-packages")]
[Authorize("Admin")]
public sealed class AdminCoinPackagesController : ApiController
{
    private readonly IMapper _mapper;

    public AdminCoinPackagesController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<AdminCoinPackageResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminCoinPackagesQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<CoinPackage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertCoinPackageRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateCoinPackageCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{packageId:guid}")]
    [ProducesResponseType(typeof(Result<CoinPackage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid packageId,
        [FromBody] UpsertCoinPackageRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<UpdateCoinPackageCommand>(request) with { Id = packageId };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{packageId:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid packageId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteCoinPackageCommand(packageId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record UpsertCoinPackageRequest(
    string Name,
    decimal CoinAmount,
    decimal BonusCoins,
    decimal Price,
    string Currency,
    bool IsActive,
    int DisplayOrder);
