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

            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(MapResources(response));
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourcePresignResult>>(
                new Error(
                    ex.StatusCode == StatusCode.NotFound ? "UserResources.NotFound" : "UserResources.GrpcError",
                    ex.Status.Detail));
        }
    }

    public async Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPublicPresignedResourcesAsync(
        IReadOnlyCollection<Guid> resourceIds,
        CancellationToken cancellationToken)
    {
        if (resourceIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(Array.Empty<UserResourcePresignResult>());
        }

        try
        {
            var response = await _client.GetPublicResourcesAsync(new GetPublicResourcesRequest
            {
                ResourceIds = { resourceIds.Select(id => id.ToString()) }
            }, cancellationToken: cancellationToken);

            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(MapResources(response));
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourcePresignResult>>(
                new Error(
                    ex.StatusCode == StatusCode.NotFound ? "UserResources.NotFound" : "UserResources.GrpcError",
                    ex.Status.Detail));
        }
    }

    public async Task<Result<PublicUserProfileResult>> GetPublicUserProfileByUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetPublicUserProfileByUsernameAsync(
                new GetPublicUserProfileByUsernameRequest
                {
                    Username = username
                },
                cancellationToken: cancellationToken);

            if (!Guid.TryParse(response.UserId, out var userId) || userId == Guid.Empty)
            {
                return Result.Failure<PublicUserProfileResult>(
                    new Error("UserResources.InvalidUserId", "The public profile response contained an invalid user id."));
            }

            return Result.Success(new PublicUserProfileResult(
                userId,
                response.Username,
                string.IsNullOrWhiteSpace(response.FullName) ? null : response.FullName,
                string.IsNullOrWhiteSpace(response.AvatarUrl) ? null : response.AvatarUrl));
        }
        catch (RpcException ex)
        {
            return Result.Failure<PublicUserProfileResult>(
                new Error(
                    ex.StatusCode == StatusCode.NotFound ? "UserResources.NotFound" : "UserResources.GrpcError",
                    ex.Status.Detail));
        }
    }

    private static IReadOnlyList<UserResourcePresignResult> MapResources(GetPresignedResourcesResponse response)
    {
        return response.Resources
            .Select(item => new UserResourcePresignResult(
                Guid.TryParse(item.ResourceId, out var resourceId) ? resourceId : Guid.Empty,
                item.PresignedUrl,
                item.ContentType,
                item.ResourceType))
            .Where(item => item.ResourceId != Guid.Empty)
            .ToList();
    }
}
