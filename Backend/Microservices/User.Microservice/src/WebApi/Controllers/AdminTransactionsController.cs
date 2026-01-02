using Application.Transactions.Commands;
using Application.Transactions.Queries;
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
[Route("api/User/admin/transactions")]
[Authorize("ADMIN", "Admin")]
public sealed class AdminTransactionsController : ApiController
{
    private readonly IMapper _mapper;

    public AdminTransactionsController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<Transaction>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeDeleted, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTransactionsQuery(includeDeleted), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<Transaction>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTransactionByIdQuery(id, includeDeleted), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<Transaction>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateTransactionCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Result<Transaction>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<UpdateTransactionCommand>(request) with { Id = id };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(Result<Transaction>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] PatchTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<PatchTransactionCommand>(request) with { Id = id };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteTransactionCommand(id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record CreateTransactionRequest(
    Guid UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status);

public sealed record UpdateTransactionRequest(
    Guid UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status);

public sealed record PatchTransactionRequest(
    Guid? UserId,
    Guid? RelationId,
    string? RelationType,
    decimal? Cost,
    string? TransactionType,
    int? TokenUsed,
    string? PaymentMethod,
    string? Status);
