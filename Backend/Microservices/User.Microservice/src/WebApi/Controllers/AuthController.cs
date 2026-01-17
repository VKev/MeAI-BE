using Application.Users.Commands;
using Application.Users.Models;
using Application.Users.Queries;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using System.Security.Claims;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/auth")]
public sealed class AuthController : ApiController
{
    private readonly IMapper _mapper;
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";
    private const int RefreshTokenDays = 7;

    public AuthController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<RegisterUserCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<LoginWithPasswordCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result);
    }

    [HttpPost("login/google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LoginWithGoogle(
        [FromBody] GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<LoginWithGoogleCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new MessageResponse("Missing refresh token"));
        }

        var result = await _mediator.Send(new RefreshTokenCommand(refreshToken), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        SetAuthCookies(result.Value);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(Result<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetMeQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var accessToken = TryGetAccessToken();
        var refreshToken = Request.Cookies.TryGetValue(RefreshTokenCookie, out var refreshCookie)
            ? refreshCookie
            : null;

        var result = await _mediator.Send(new LogoutCommand(accessToken, refreshToken), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        ClearAuthCookies();
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<ForgotPasswordCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<ResetPasswordCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("send-verification-code")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendVerificationCode(
        [FromBody] SendVerificationCodeRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<SendEmailVerificationCodeCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<VerifyEmailCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(Result<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EditProfile(
        [FromBody] EditProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var command = _mapper.Map<EditProfileCommand>(request) with { UserId = userId };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }


    private void SetAuthCookies(LoginResponse response)
    {
        var secure = Request.IsHttps;
        var sameSite = secure ? SameSiteMode.None : SameSiteMode.Lax;
        var accessExpiry = new DateTimeOffset(DateTime.SpecifyKind(response.ExpiresAt, DateTimeKind.Utc));

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
                Path = "/api/User/auth/refresh"
            });
    }

    private void ClearAuthCookies()
    {
        var secure = Request.IsHttps;
        var sameSite = secure ? SameSiteMode.None : SameSiteMode.Lax;

        Response.Cookies.Delete(
            AccessTokenCookie,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Path = "/"
            });

        Response.Cookies.Delete(
            RefreshTokenCookie,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Path = "/api/User/auth/refresh"
            });
    }

    private string? TryGetAccessToken()
    {
        if (Request.Cookies.TryGetValue(AccessTokenCookie, out var accessToken) &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken.Trim();
        }

        var authorizationHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authorizationHeader))
        {
            return null;
        }

        if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorizationHeader["Bearer ".Length..].Trim();
        }

        return null;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}

public sealed record LoginRequest(string EmailOrUsername, string Password);

public sealed record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string Code,
    string? FullName,
    string? PhoneNumber);

public sealed record GoogleLoginRequest(string IdToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);

public sealed record SendVerificationCodeRequest(string Email);

public sealed record VerifyEmailRequest(string Email, string Code);

public sealed record EditProfileRequest(
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId);

