using Application.Resources.Queries;
using Grpc.Core;
using MediatR;
using SharedLibrary.Grpc.UserResources;

namespace WebApi.Grpc;

public sealed class UserResourceGrpcService : UserResourceService.UserResourceServiceBase
{
    private readonly IMediator _mediator;

    public UserResourceGrpcService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetPresignedResourcesResponse> GetPresignedResources(
        GetPresignedResourcesRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var resourceIds = new List<Guid>();
        foreach (var resourceId in request.ResourceIds)
        {
            if (!Guid.TryParse(resourceId, out var parsedId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resourceId."));
            }

            resourceIds.Add(parsedId);
        }

        var result = await _mediator.Send(
            new GetResourcesByIdsQuery(userId, resourceIds),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        var response = new GetPresignedResourcesResponse();
        response.Resources.AddRange(result.Value.Select(resource => new PresignedResource
        {
            ResourceId = resource.Id.ToString(),
            PresignedUrl = resource.PresignedUrl,
            ContentType = resource.ContentType ?? string.Empty,
            ResourceType = resource.ResourceType ?? string.Empty
        }));

        return response;
    }
}
