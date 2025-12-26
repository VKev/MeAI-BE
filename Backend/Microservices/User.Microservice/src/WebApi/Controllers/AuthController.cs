using System;
using Application.Users.Commands.Login;
using Application.Users.Commands.Refresh;
using Application.Users.Commands.Register;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Authentication;
using SharedLibrary.Common;

namespace WebApi.Controllers;

[Route("[controller]")]
public class AuthController(IMediator mediator) : ApiController(mediator)
{
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";
    private const int RefreshTokenDays = 7;

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(request, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result.Value);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginWithPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(request, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result.Value);
    }

    [HttpPost("login/google")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginWithGoogle([FromBody] GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new LoginWithGoogleCommand(request.IdToken), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result.Value);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { message = "Missing refresh token" });
        }

        var result = await _mediator.Send(new RefreshTokenCommand(refreshToken), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result.Value);
    }

    private void SetAuthCookies(LoginResponse response)
    {
        var secure = Request.IsHttps;
        var sameSite = secure ? SameSiteMode.None : SameSiteMode.Lax;

        var accessExpiry = new DateTimeOffset(
            DateTime.SpecifyKind(response.ExpiresAt, DateTimeKind.Utc));

        Response.Cookies.Append(
            AccessTokenCookie,
            response.AccessToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Expires = accessExpiry,
                Path = "/"
            });

        Response.Cookies.Append(
            RefreshTokenCookie,
            response.RefreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Expires = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
                Path = "/api/auth/refresh"
            });
    }
}

public sealed record GoogleLoginRequest(string IdToken);
