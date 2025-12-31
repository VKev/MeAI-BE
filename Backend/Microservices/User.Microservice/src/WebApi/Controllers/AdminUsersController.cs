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

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/admin/users")]
[Authorize("ADMIN", "Admin")]
public sealed class AdminUsersController : ApiController
{
    private readonly IMapper _mapper;

    public AdminUsersController(IMediator mediator, IMapper mapper)
        : base(mediator)
    {
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<AdminUserResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeDeleted, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUsersQuery(includeDeleted), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<AdminUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserByIdQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<AdminUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<CreateUserCommand>(request);
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Result<AdminUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<UpdateUserCommand>(request) with { UserId = id };
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(Result<AdminUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetRole(Guid id, [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var command = _mapper.Map<SetUserRoleCommand>(request) with { UserId = id };
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
        var result = await _mediator.Send(new DeleteUserCommand(id), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
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
