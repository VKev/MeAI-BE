using Application.Users.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand request, CancellationToken cancellationToken)
    {
        var response = await _sender.Send(request, cancellationToken);
        if (response == null)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        return Ok(response);
    }
}
