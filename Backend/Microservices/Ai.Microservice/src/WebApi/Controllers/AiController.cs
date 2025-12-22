using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Ais.Commands;
using Application.Ais.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Common;
using SharedLibrary.Attributes;

namespace WebApi.Controllers
{
    [Route("api/[controller]")]
    public class AiController : ApiController
    {
        public AiController(IMediator mediator) : base(mediator)
        {
        }

        [HttpPost("create")]
        [Authorize("Admin", "User")]
        public async Task<IActionResult> Create([FromBody] CreateAiCommand request, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(request, cancellationToken);
            if (result.IsFailure)
            {
                return HandleFailure(result);
            }

            return Ok(result);
        }

        [HttpGet("read")]
        [Authorize("Admin", "User")]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetAllAisQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok();
        }
    }
}
