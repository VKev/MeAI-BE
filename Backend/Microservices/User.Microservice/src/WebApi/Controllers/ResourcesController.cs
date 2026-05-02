using Application.Abstractions.Storage;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Application.Resources.Commands;
using Application.Resources.Models;
using Application.Resources.Queries;
using Application.Users.Models;
using Infrastructure.Configuration;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using SharedLibrary.Attributes;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using AuthorizeAttribute = SharedLibrary.Attributes.AuthorizeAttribute;
using AllowAnonymousAttribute = SharedLibrary.Attributes.AllowAnonymousAttribute;

namespace WebApi.Controllers;

[ApiController]
[Route("api/User/resources")]
[Authorize]
public sealed class ResourcesController : ApiController
{
    private readonly FeedSeedOptions _feedSeedOptions;

    public ResourcesController(
        IMediator mediator,
        IOptions<FeedSeedOptions> feedSeedOptions)
        : base(mediator)
    {
        _feedSeedOptions = feedSeedOptions.Value;
    }

    [AllowAnonymous]
    [HttpGet("~/api/User/seed-media/{*fileName}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetSeedMedia(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Seed media not found",
                Detail = "File name is required."
            });
        }

        var mediaRoot = Path.Combine(ResolveDataRoot(_feedSeedOptions.DataRoot), "media");
        if (!Directory.Exists(mediaRoot))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Seed media not found",
                Detail = "Seed media directory does not exist."
            });
        }

        var sanitizedRelativePath = fileName
            .Replace('\u005c', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var mediaRootFullPath = Path.GetFullPath(mediaRoot);
        var targetPath = Path.GetFullPath(Path.Combine(mediaRootFullPath, sanitizedRelativePath));
        if (!targetPath.StartsWith(mediaRootFullPath, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(targetPath))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Seed media not found",
                Detail = "The requested seed media file was not found."
            });
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(targetPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return PhysicalFile(targetPath, contentType, enableRangeProcessing: true);
    }

    [HttpGet]
    [ProducesResponseType(typeof(Result<List<ResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
            [FromQuery] DateTime? cursorCreatedAt,
            [FromQuery] Guid? cursorId,
            [FromQuery] int? limit,
            [FromQuery] string[]? originKinds,
            CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new GetResourcesQuery(userId, cursorCreatedAt, cursorId, limit, originKinds),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("workspace/{workspaceId:guid}")]
    [ProducesResponseType(typeof(Result<List<ResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByWorkspace(
        Guid workspaceId,
        [FromQuery] DateTime? cursorCreatedAt,
        [FromQuery] Guid? cursorId,
        [FromQuery] int? limit,
        [FromQuery] string[]? originKinds,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(
            new GetWorkspaceResourcesQuery(userId, workspaceId, cursorCreatedAt, cursorId, limit, originKinds),
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetResourceByIdQuery(id, userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpGet("storage-usage")]
    [ProducesResponseType(typeof(Result<StorageUsageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStorageUsage(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new GetStorageUsageQuery(userId), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Result<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromForm] IFormFile file,
        [FromForm] string? status,
        [FromForm] string? resourceType,
        [FromForm] string? workspaceId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid file",
                Detail = "File is required"
            });
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        Guid? normalizedWorkspaceId = null;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            if (!Guid.TryParse(workspaceId, out var parsedWorkspaceId) || parsedWorkspaceId == Guid.Empty)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid workspaceId",
                    Detail = "workspaceId must be a valid GUID."
                });
            }

            normalizedWorkspaceId = parsedWorkspaceId;
        }

        var command = new UploadResourceFileCommand(
            userId,
            file.OpenReadStream(),
            file.FileName,
            contentType,
            file.Length,
            status,
            resourceType,
            normalizedWorkspaceId);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Result<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] string? status,
        [FromForm] string? resourceType,
        [FromForm] string? workspaceId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid file",
                Detail = "File is required"
            });
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        Guid? normalizedWorkspaceId = null;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            if (!Guid.TryParse(workspaceId, out var parsedWorkspaceId) || parsedWorkspaceId == Guid.Empty)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid workspaceId",
                    Detail = "workspaceId must be a valid GUID."
                });
            }

            normalizedWorkspaceId = parsedWorkspaceId;
        }

        var command = new UpdateResourceFileCommand(
            id,
            userId,
            file.OpenReadStream(),
            file.FileName,
            contentType,
            file.Length,
            status,
            resourceType,
            normalizedWorkspaceId);

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new MessageResponse("Unauthorized"));
        }

        var result = await _mediator.Send(new DeleteResourceCommand(id, userId), cancellationToken);
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

    private static string ResolveDataRoot(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath("/seed-data/feed");
        }

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }
}
