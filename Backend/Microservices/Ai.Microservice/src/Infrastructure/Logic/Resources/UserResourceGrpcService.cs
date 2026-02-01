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
        IReadOnlyList<Guid> resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.Count == 0)
        {
            return Result.Failure<IReadOnlyList<UserResourcePresignResult>>(
                new Error("UserResources.Missing", "At least one resource is required."));
        }

        var request = new GetPresignedResourcesRequest
        {
            UserId = userId.ToString()
        };

        request.ResourceIds.AddRange(resourceIds.Select(id => id.ToString()));

        try
        {
            var response = await _client.GetPresignedResourcesAsync(request, cancellationToken: cancellationToken);
            var result = response.Resources.Select(resource => new UserResourcePresignResult(
                Guid.Parse(resource.ResourceId),
                resource.PresignedUrl,
                string.IsNullOrWhiteSpace(resource.ContentType) ? null : resource.ContentType,
                string.IsNullOrWhiteSpace(resource.ResourceType) ? null : resource.ResourceType)).ToList();

            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(result);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourcePresignResult>>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }

    public async Task<Result<IReadOnlyList<UserResourceCreatedResult>>> CreateResourcesFromUrlsAsync(
        Guid userId,
        IReadOnlyList<string> urls,
        string? status,
        string? resourceType,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            return Result.Failure<IReadOnlyList<UserResourceCreatedResult>>(
                new Error("UserResources.Missing", "At least one resource URL is required."));
        }

        var request = new CreateResourcesFromUrlsRequest
        {
            UserId = userId.ToString(),
            Status = status ?? string.Empty,
            ResourceType = resourceType ?? string.Empty
        };

        request.Urls.AddRange(urls.Where(url => !string.IsNullOrWhiteSpace(url)));

        if (request.Urls.Count == 0)
        {
            return Result.Failure<IReadOnlyList<UserResourceCreatedResult>>(
                new Error("UserResources.Missing", "At least one resource URL is required."));
        }

        try
        {
            var response = await _client.CreateResourcesFromUrlsAsync(request, cancellationToken: cancellationToken);
            var result = response.Resources.Select(resource => new UserResourceCreatedResult(
                Guid.Parse(resource.ResourceId),
                resource.PresignedUrl,
                string.IsNullOrWhiteSpace(resource.ContentType) ? null : resource.ContentType,
                string.IsNullOrWhiteSpace(resource.ResourceType) ? null : resource.ResourceType)).ToList();

            return Result.Success<IReadOnlyList<UserResourceCreatedResult>>(result);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourceCreatedResult>>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }
}
