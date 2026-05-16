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

    public async Task<Result<int>> DeleteResourcesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> resourceIds,
        CancellationToken cancellationToken)
    {
        var normalizedResourceIds = resourceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedResourceIds.Count == 0)
        {
            return Result.Success(0);
        }

        try
        {
            var response = await _client.DeleteResourcesAsync(new DeleteResourcesRequest
            {
                UserId = userId.ToString(),
                ResourceIds = { normalizedResourceIds.Select(id => id.ToString()) }
            }, cancellationToken: cancellationToken);

            return Result.Success(response.DeletedCount);
        }
        catch (RpcException ex)
        {
            return Result.Failure<int>(
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

            var profileResult = MapPublicUserProfile(response.UserId, response.Username, response.FullName, response.AvatarUrl);
            return profileResult.IsFailure
                ? Result.Failure<PublicUserProfileResult>(profileResult.Error)
                : Result.Success(profileResult.Value);
        }
        catch (RpcException ex)
        {
            return Result.Failure<PublicUserProfileResult>(
                new Error(
                    ex.StatusCode == StatusCode.NotFound ? "UserResources.NotFound" : "UserResources.GrpcError",
                    ex.Status.Detail));
        }
    }

    public async Task<Result<IReadOnlyDictionary<Guid, PublicUserProfileResult>>> GetPublicUserProfilesByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(
                new Dictionary<Guid, PublicUserProfileResult>());
        }

        try
        {
            var response = await _client.GetPublicUserProfilesByIdsAsync(
                new GetPublicUserProfilesByIdsRequest
                {
                    UserIds = { userIds.Where(id => id != Guid.Empty).Distinct().Select(id => id.ToString()) }
                },
                cancellationToken: cancellationToken);

            var profiles = new Dictionary<Guid, PublicUserProfileResult>();
            foreach (var profile in response.Profiles)
            {
                var profileResult = MapPublicUserProfile(profile.UserId, profile.Username, profile.FullName, profile.AvatarUrl);
                if (profileResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(profileResult.Error);
                }

                profiles[profileResult.Value.UserId] = profileResult.Value;
            }

            return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(profiles);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(
                new Error(
                    ex.StatusCode == StatusCode.NotFound ? "UserResources.NotFound" : "UserResources.GrpcError",
                    ex.Status.Detail));
        }
    }

    private static Result<PublicUserProfileResult> MapPublicUserProfile(
        string userIdValue,
        string username,
        string fullName,
        string avatarUrl)
    {
        if (!Guid.TryParse(userIdValue, out var userId) || userId == Guid.Empty)
        {
            return Result.Failure<PublicUserProfileResult>(
                new Error("UserResources.InvalidUserId", "The public profile response contained an invalid user id."));
        }

        return Result.Success(new PublicUserProfileResult(
            userId,
            username,
            string.IsNullOrWhiteSpace(fullName) ? null : fullName,
            string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl));
    }

    private static IReadOnlyList<UserResourcePresignResult> MapResources(GetPresignedResourcesResponse response)
    {
        return response.Resources
            .Select(item => new UserResourcePresignResult(
                Guid.TryParse(item.ResourceId, out var resourceId) ? resourceId : Guid.Empty,
                item.PresignedUrl,
                item.ContentType,
                item.ResourceType,
                string.IsNullOrWhiteSpace(item.OriginKind) ? null : item.OriginKind,
                string.IsNullOrWhiteSpace(item.OriginSourceUrl) ? null : item.OriginSourceUrl,
                ParseOptionalGuid(item.OriginChatSessionId),
                ParseOptionalGuid(item.OriginChatId)))
            .Where(item => item.ResourceId != Guid.Empty)
            .ToList();
    }

    private static Guid? ParseOptionalGuid(string value)
    {
        return Guid.TryParse(value, out var parsedId) && parsedId != Guid.Empty
            ? parsedId
            : null;
    }
}
