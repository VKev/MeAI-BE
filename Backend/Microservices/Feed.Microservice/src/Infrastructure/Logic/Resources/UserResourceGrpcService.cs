using Application.Abstractions.Resources;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.UserResources;

namespace Infrastructure.Logic.Resources;

public sealed class UserResourceGrpcService : IUserResourceService
{
    private readonly UserResourceService.UserResourceServiceClient _client;

    public UserResourceGrpcService(UserResourceService.UserResourceServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPresignedResourcesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(Array.Empty<UserResourcePresignResult>());
        }

        try
        {
            var response = await _client.GetPresignedResourcesAsync(new GetPresignedResourcesRequest
            {
                UserId = userId.ToString(),
                ResourceIds = { resourceIds.Select(id => id.ToString()) }
            }, cancellationToken: cancellationToken);

            var resources = response.Resources
                .Select(item => new UserResourcePresignResult(
                    Guid.TryParse(item.ResourceId, out var resourceId) ? resourceId : Guid.Empty,
                    item.PresignedUrl,
                    item.ContentType,
                    item.ResourceType))
                .Where(item => item.ResourceId != Guid.Empty)
                .ToList();

            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(resources);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourcePresignResult>>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }
}
