using Application.Admin.Models;
using Application.Admin.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/admin/spending/ai")]
[Authorize("ADMIN", "Admin", "admin")]
public sealed class AdminAiSpendingController : ApiController
{
    public AdminAiSpendingController(IMediator mediator)
        : base(mediator)
    {
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<AdminAiSpendOverviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminAiSpendOverviewQuery(period), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}
