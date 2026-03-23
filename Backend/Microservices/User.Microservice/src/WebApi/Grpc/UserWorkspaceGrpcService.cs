using Application.Workspaces.Queries;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.UserResources;

namespace WebApi.Grpc;

public sealed class UserWorkspaceGrpcService : UserWorkspaceService.UserWorkspaceServiceBase
{
    private readonly IMediator _mediator;

    public UserWorkspaceGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetWorkspaceByIdResponse> GetWorkspaceById(
        GetWorkspaceByIdRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!Guid.TryParse(request.WorkspaceId, out var workspaceId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid workspaceId."));
        }

        var result = await _mediator.Send(
            new GetWorkspaceByIdQuery(workspaceId, userId),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        return new GetWorkspaceByIdResponse
        {
            WorkspaceId = result.Value.Id.ToString(),
            Name = result.Value.Name,
            Type = result.Value.Type ?? string.Empty,
            Description = result.Value.Description ?? string.Empty,
            CreatedAt = result.Value.CreatedAt?.ToString("O") ?? string.Empty,
            UpdatedAt = result.Value.UpdatedAt?.ToString("O") ?? string.Empty
        };
    }
}
