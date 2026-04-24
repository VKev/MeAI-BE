using Application.Resources.Models;
using Application.Resources.Queries;
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

    [HttpGet("overview")]
    [ProducesResponseType(typeof(Result<AdminStorageOverviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminStorageOverviewQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}
