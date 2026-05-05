using Application.Abstractions.Resources;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Common.Resources;
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
                string.IsNullOrWhiteSpace(resource.ResourceType) ? null : resource.ResourceType,
                string.IsNullOrWhiteSpace(resource.OriginKind) ? null : resource.OriginKind,
                string.IsNullOrWhiteSpace(resource.OriginSourceUrl) ? null : resource.OriginSourceUrl,
                ParseOptionalGuid(resource.OriginChatSessionId),
                ParseOptionalGuid(resource.OriginChatId))).ToList();

            return Result.Success<IReadOnlyList<UserResourcePresignResult>>(result);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourcePresignResult>>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }

    public async Task<Result<StorageQuotaCheckResult>> CheckStorageQuotaAsync(
        Guid userId,
        long requestedBytes,
        string purpose,
        int estimatedFileCount,
        CancellationToken cancellationToken,
        Guid? workspaceId = null)
    {
        var request = new CheckStorageQuotaRequest
        {
            UserId = userId.ToString(),
            RequestedBytes = requestedBytes,
            Purpose = purpose ?? string.Empty,
            EstimatedFileCount = estimatedFileCount,
            WorkspaceId = workspaceId?.ToString() ?? string.Empty
        };

        try
        {
            var response = await _client.CheckStorageQuotaAsync(request, cancellationToken: cancellationToken);
            return Result.Success(new StorageQuotaCheckResult(
                response.Allowed,
                response.QuotaBytes == 0 ? null : response.QuotaBytes,
                response.UsedBytes,
                response.ReservedBytes,
                response.AvailableBytes,
                response.MaxUploadFileBytes == 0 ? null : response.MaxUploadFileBytes,
                response.SystemStorageQuotaBytes == 0 ? null : response.SystemStorageQuotaBytes,
                string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
                string.IsNullOrWhiteSpace(response.ErrorMessage) ? null : response.ErrorMessage));
        }
        catch (RpcException ex)
        {
            return Result.Failure<StorageQuotaCheckResult>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }

    public async Task<Result<IReadOnlyList<UserResourceCreatedResult>>> CreateResourcesFromUrlsAsync(
        Guid userId,
        IReadOnlyList<string> urls,
        string? status,
        string? resourceType,
        CancellationToken cancellationToken,
        Guid? workspaceId = null,
        ResourceProvenanceMetadata? provenance = null)
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
            ResourceType = resourceType ?? string.Empty,
            WorkspaceId = workspaceId.HasValue ? workspaceId.Value.ToString() : string.Empty,
            OriginKind = provenance?.OriginKind ?? string.Empty,
            OriginChatSessionId = provenance?.OriginChatSessionId?.ToString() ?? string.Empty,
            OriginChatId = provenance?.OriginChatId?.ToString() ?? string.Empty
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
                string.IsNullOrWhiteSpace(resource.ResourceType) ? null : resource.ResourceType,
                string.IsNullOrWhiteSpace(resource.OriginKind) ? null : resource.OriginKind,
                string.IsNullOrWhiteSpace(resource.OriginSourceUrl) ? null : resource.OriginSourceUrl,
                ParseOptionalGuid(resource.OriginChatSessionId),
                ParseOptionalGuid(resource.OriginChatId))).ToList();

            return Result.Success<IReadOnlyList<UserResourceCreatedResult>>(result);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyList<UserResourceCreatedResult>>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }

    public async Task<Result<int>> BackfillResourceProvenanceAsync(
        IReadOnlyList<ResourceProvenanceBackfillRequest> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return Result.Success(0);
        }

        var request = new BackfillResourceProvenanceRequest();
        request.Items.AddRange(items
            .Where(item => item.ResourceId != Guid.Empty)
            .Select(item => new SharedLibrary.Grpc.UserResources.ResourceProvenanceBackfillItem
            {
                ResourceId = item.ResourceId.ToString(),
                OriginKind = item.OriginKind ?? string.Empty,
                OriginSourceUrl = item.OriginSourceUrl ?? string.Empty,
                OriginChatSessionId = item.OriginChatSessionId?.ToString() ?? string.Empty,
                OriginChatId = item.OriginChatId?.ToString() ?? string.Empty
            }));

        if (request.Items.Count == 0)
        {
            return Result.Success(0);
        }

        try
        {
            var response = await _client.BackfillResourceProvenanceAsync(request, cancellationToken: cancellationToken);
            return Result.Success(response.UpdatedCount);
        }
        catch (RpcException ex)
        {
            return Result.Failure<int>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
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

        var request = new GetPublicUserProfilesByIdsRequest();
        request.UserIds.AddRange(userIds.Select(id => id.ToString()));

        try
        {
            var response = await _client.GetPublicUserProfilesByIdsAsync(request, cancellationToken: cancellationToken);
            var result = response.Profiles
                .Select(profile => new
                {
                    IsValid = Guid.TryParse(profile.UserId, out var parsedId),
                    ParsedId = parsedId,
                    Profile = profile
                })
                .Where(item => item.IsValid)
                .ToDictionary(
                    item => item.ParsedId,
                    item => new PublicUserProfileResult(
                        item.ParsedId,
                        item.Profile.Username,
                        string.IsNullOrWhiteSpace(item.Profile.FullName) ? null : item.Profile.FullName,
                        string.IsNullOrWhiteSpace(item.Profile.AvatarUrl) ? null : item.Profile.AvatarUrl));

            return Result.Success<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(result);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlyDictionary<Guid, PublicUserProfileResult>>(
                new Error("UserResources.GrpcError", ex.Status.Detail));
        }
    }

    private static Guid? ParseOptionalGuid(string value)
    {
        return Guid.TryParse(value, out var parsedId) && parsedId != Guid.Empty
            ? parsedId
            : null;
    }
}
