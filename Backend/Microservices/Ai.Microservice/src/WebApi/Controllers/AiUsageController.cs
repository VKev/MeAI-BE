using System.Security.Claims;
using Application.Usage.Models;
using Application.Usage.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/usage")]
[Authorize]
public sealed class AiUsageController : ApiController
{
    public AiUsageController(IMediator mediator)
        : base(mediator)
    {
    }

    [HttpGet("history")]
    [ProducesResponseType(typeof(Result<AiUsageHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] AiUsageHistoryQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Unauthorized" });
        }

        var filterResult = parameters.ToFilter();
        if (filterResult.IsFailure)
        {
            return HandleFailure(filterResult);
        }

        var result = await _mediator.Send(new GetMyAiUsageHistoryQuery(userId, filterResult.Value), cancellationToken);
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
