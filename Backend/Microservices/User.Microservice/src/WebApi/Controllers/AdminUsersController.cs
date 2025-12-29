using Application.Users.Commands.CreateUser;
using Application.Users.Commands.DeleteUser;
using Application.Users.Commands.SetUserRole;
using Application.Users.Commands.UpdateUser;
using Application.Users.Contracts;
using Application.Users.Queries.GetUserById;
using Application.Users.Queries.GetUsers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using WebApi.Contracts;

namespace WebApi.Controllers;

[Route("admin/users")]
[Authorize("ADMIN")]
public sealed class AdminUsersController(IMediator mediator) : ApiController(mediator)
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeDeleted, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUsersQuery(includeDeleted), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserByIdQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateUserCommand(
            request.Username,
            request.Email,
            request.Password,
            request.FullName,
            request.PhoneNumber,
            request.Address,
            request.Birthday,
            request.AvatarResourceId,
            request.MeAiCoin,
            request.EmailVerified,
            request.Role), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateUserCommand(
            id,
            request.Username,
            request.Email,
            request.Password,
            request.FullName,
            request.PhoneNumber,
            request.Address,
            request.Birthday,
            request.AvatarResourceId,
            request.MeAiCoin,
            request.EmailVerified), cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetRole(Guid id, [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetUserRoleCommand(id, request.Role), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteUserCommand(id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }
}

public sealed record CreateAdminUserRequest(
    string Username,
    string Email,
    string Password,
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId,
    decimal? MeAiCoin,
    bool? EmailVerified,
    string? Role);

public sealed record UpdateAdminUserRequest(
    string? Username,
    string? Email,
    string? Password,
    string? FullName,
    string? PhoneNumber,
    string? Address,
    DateTime? Birthday,
    Guid? AvatarResourceId,
    decimal? MeAiCoin,
    bool? EmailVerified);

public sealed record UpdateUserRoleRequest(string Role);
