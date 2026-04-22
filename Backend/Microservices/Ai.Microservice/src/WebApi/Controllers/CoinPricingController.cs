using Application.Billing;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/coin-pricing")]
public sealed class CoinPricingController : ApiController
{
    private readonly ICoinPricingRepository _repository;
    private readonly ICoinPricingService _pricingService;

    public CoinPricingController(
        IMediator mediator,
        ICoinPricingRepository repository,
        ICoinPricingService pricingService) : base(mediator)
    {
        _repository = repository;
        _pricingService = pricingService;
    }

    // Public catalog — FE uses it to show "Generate (N coins)" on buttons.
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(Result<IEnumerable<CoinPricingResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var entries = await _repository.GetActiveAsync(cancellationToken);
        return Ok(Result.Success<IEnumerable<CoinPricingResponse>>(entries.Select(MapToResponse).ToList()));
    }

    // Cost quote for a specific generation request. Lets the FE preview the charge BEFORE
    // the user clicks Generate so they can top up if short.
    [HttpPost("estimate")]
    [Authorize]
    [ProducesResponseType(typeof(Result<CoinCostQuote>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Estimate(
        [FromBody] EstimateCostRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _pricingService.GetCostAsync(
            request.ActionType,
            request.Model,
            request.Variant,
            request.Quantity <= 0 ? 1 : request.Quantity,
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    // --- admin CRUD ---

    [HttpPost]
    [Authorize("admin")]
    [ProducesResponseType(typeof(Result<CoinPricingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertCoinPricingRequest request,
        CancellationToken cancellationToken)
    {
        var entry = new CoinPricingCatalogEntry
        {
            Id = Guid.CreateVersion7(),
            ActionType = request.ActionType.Trim(),
            Model = request.Model.Trim(),
            Variant = string.IsNullOrWhiteSpace(request.Variant) ? null : request.Variant.Trim(),
            Unit = request.Unit.Trim(),
            UnitCostCoins = request.UnitCostCoins,
            IsActive = request.IsActive,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(entry, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(MapToResponse(entry)));
    }

    [HttpPut("{id:guid}")]
    [Authorize("admin")]
    [ProducesResponseType(typeof(Result<CoinPricingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpsertCoinPricingRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await _repository.GetByIdAsync(id, cancellationToken);
        if (entry is null)
        {
            return HandleFailure(Result.Failure<CoinPricingResponse>(
                new Error("Pricing.NotFound", "Pricing entry not found.")));
        }

        entry.ActionType = request.ActionType.Trim();
        entry.Model = request.Model.Trim();
        entry.Variant = string.IsNullOrWhiteSpace(request.Variant) ? null : request.Variant.Trim();
        entry.Unit = request.Unit.Trim();
        entry.UnitCostCoins = request.UnitCostCoins;
        entry.IsActive = request.IsActive;
        entry.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _repository.Update(entry);
        await _repository.SaveChangesAsync(cancellationToken);

        return Ok(Result.Success(MapToResponse(entry)));
    }

    [HttpDelete("{id:guid}")]
    [Authorize("admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entry = await _repository.GetByIdAsync(id, cancellationToken);
        if (entry is null)
        {
            return HandleFailure(Result.Failure<bool>(
                new Error("Pricing.NotFound", "Pricing entry not found.")));
        }

        // Soft-deactivate — keeps the ledger relationship auditable. Admins can toggle
        // IsActive back on via PUT without recreating the row.
        entry.IsActive = false;
        entry.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(entry);
        await _repository.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static CoinPricingResponse MapToResponse(CoinPricingCatalogEntry e) => new(
        e.Id, e.ActionType, e.Model, e.Variant, e.Unit, e.UnitCostCoins, e.IsActive, e.CreatedAt, e.UpdatedAt);
}

public sealed record EstimateCostRequest(
    string ActionType,
    string Model,
    string? Variant,
    int Quantity);

public sealed record UpsertCoinPricingRequest(
    string ActionType,
    string Model,
    string? Variant,
    string Unit,
    decimal UnitCostCoins,
    bool IsActive);

public sealed record CoinPricingResponse(
    Guid Id,
    string ActionType,
    string Model,
    string? Variant,
    string Unit,
    decimal UnitCostCoins,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
