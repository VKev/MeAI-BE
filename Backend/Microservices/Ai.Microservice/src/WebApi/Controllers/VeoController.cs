using Application.Veo.Commands;
using Application.Veo.Models;
using Application.Veo.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace WebApi.Controllers;

[ApiController]
[Route("api/Ai/veo")]
public sealed class VeoController : ApiController
{
    public VeoController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost("generate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<GenerateVideoCommandResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateVideo(
        [FromBody] GenerateVideoRequest request,
        CancellationToken cancellationToken)
    {
        var command = new GenerateVideoCommand(
            Prompt: request.Prompt,
            ImageUrls: request.ImageUrls,
            Model: request.Model ?? "veo3_fast",
            GenerationType: request.GenerationType,
            AspectRatio: request.AspectRatio ?? "16:9",
            Seeds: request.Seeds,
            EnableTranslation: request.EnableTranslation ?? true,
            Watermark: request.Watermark);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost("extend")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<ExtendVideoCommandResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtendVideo(
        [FromBody] ExtendVideoRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ExtendVideoCommand(
            OriginalCorrelationId: request.CorrelationId,
            Prompt: request.Prompt,
            Seeds: request.Seeds,
            Watermark: request.Watermark);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("status/{correlationId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<VideoTaskStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var query = new GetVideoStatusQuery(correlationId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    [HttpPost("callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleCallback(
        [FromBody] VeoCallbackPayload payload,
        CancellationToken cancellationToken)
    {
        var command = new HandleVideoCallbackCommand(payload);
        var result = await _mediator.Send(command, cancellationToken);

        return Ok(result);
    }

    [HttpGet("record-info/{correlationId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<Application.Abstractions.VeoRecordInfoResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecordInfo(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var query = new GetVeoRecordInfoQuery(correlationId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("get-1080p-video/{correlationId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<Application.Abstractions.Veo1080PResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get1080PVideo(
        Guid correlationId,
        [FromQuery] int index = 0,
        CancellationToken cancellationToken = default)
    {
        var query = new Get1080PVideoQuery(correlationId, index);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }
}

public sealed record GenerateVideoRequest(
    string Prompt,
    List<string>? ImageUrls = null,
    string? Model = null,
    string? GenerationType = null,
    string? AspectRatio = null,
    int? Seeds = null,
    bool? EnableTranslation = null,
    string? Watermark = null);

public sealed record ExtendVideoRequest(
    Guid CorrelationId,
    string Prompt,
    int? Seeds = null,
    string? Watermark = null);
