using Application.Resources.Commands;
using Application.Resources.Queries;
using Application.Configs.Queries;
using Application.Users.Queries;
using Grpc.Core;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Common.Resources;
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
            ResourceType = resource.ResourceType ?? string.Empty,
            OriginKind = resource.OriginKind ?? string.Empty,
            OriginSourceUrl = resource.OriginSourceUrl ?? string.Empty,
            OriginChatSessionId = resource.OriginChatSessionId?.ToString() ?? string.Empty,
            OriginChatId = resource.OriginChatId?.ToString() ?? string.Empty
        }));

        return response;
    }

    public override async Task<GetPresignedResourcesResponse> GetPublicResources(
        GetPublicResourcesRequest request,
        ServerCallContext context)
    {
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
            new GetPublicResourcesQuery(resourceIds),
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
            ResourceType = resource.ResourceType ?? string.Empty,
            OriginKind = resource.OriginKind ?? string.Empty,
            OriginSourceUrl = resource.OriginSourceUrl ?? string.Empty,
            OriginChatSessionId = resource.OriginChatSessionId?.ToString() ?? string.Empty,
            OriginChatId = resource.OriginChatId?.ToString() ?? string.Empty
        }));

        return response;
    }

    public override async Task<GetPublicUserProfileByUsernameResponse> GetPublicUserProfileByUsername(
        GetPublicUserProfileByUsernameRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Username is required."));
        }

        var result = await _mediator.Send(
            new GetPublicUserProfileByUsernameQuery(request.Username),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        return MapProfile(result.Value);
    }

    public override async Task<GetPublicUserProfilesByIdsResponse> GetPublicUserProfilesByIds(
        GetPublicUserProfilesByIdsRequest request,
        ServerCallContext context)
    {
        var userIds = new List<Guid>();
        foreach (var userId in request.UserIds)
        {
            if (!Guid.TryParse(userId, out var parsedId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
            }

            userIds.Add(parsedId);
        }

        var result = await _mediator.Send(
            new GetPublicUserProfilesByIdsQuery(userIds),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.Internal, result.Error.Description));
        }

        var response = new GetPublicUserProfilesByIdsResponse();
        response.Profiles.AddRange(result.Value.Select(MapPublicUserProfile));
        return response;
    }

    public override async Task<CreateResourcesFromUrlsResponse> CreateResourcesFromUrls(
        CreateResourcesFromUrlsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (request.Urls.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "At least one URL is required."));
        }

        var status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status;
        var resourceType = string.IsNullOrWhiteSpace(request.ResourceType) ? null : request.ResourceType;
        var originKind = string.IsNullOrWhiteSpace(request.OriginKind) ? null : request.OriginKind.Trim();

        Guid? workspaceId = null;
        if (!string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            if (!Guid.TryParse(request.WorkspaceId, out var parsedWorkspaceId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid workspaceId."));
            }

            if (parsedWorkspaceId != Guid.Empty)
            {
                workspaceId = parsedWorkspaceId;
            }
        }

        var originChatSessionId = ParseOptionalGuid(request.OriginChatSessionId, "originChatSessionId");
        if (originChatSessionId.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, originChatSessionId.Error.Description));
        }

        var originChatId = ParseOptionalGuid(request.OriginChatId, "originChatId");
        if (originChatId.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, originChatId.Error.Description));
        }

        var provenance = new ResourceProvenanceMetadata(
            originKind,
            originChatSessionId.Value,
            originChatId.Value);

        var response = new CreateResourcesFromUrlsResponse();

        foreach (var url in request.Urls)
        {
            var result = await _mediator.Send(
                new UploadResourceFromUrlCommand(userId, url, status, resourceType, workspaceId, provenance with
                {
                    OriginSourceUrl = url
                }),
                context.CancellationToken);

            if (result.IsFailure)
            {
                throw new RpcException(new Status(StatusCode.Internal, result.Error.Description));
            }

            response.Resources.Add(new CreatedResource
            {
                ResourceId = result.Value.Id.ToString(),
                PresignedUrl = result.Value.Link ?? string.Empty,
                ContentType = result.Value.ContentType ?? string.Empty,
                ResourceType = result.Value.ResourceType ?? string.Empty,
                OriginKind = result.Value.OriginKind ?? string.Empty,
                OriginSourceUrl = result.Value.OriginSourceUrl ?? string.Empty,
                OriginChatSessionId = result.Value.OriginChatSessionId?.ToString() ?? string.Empty,
                OriginChatId = result.Value.OriginChatId?.ToString() ?? string.Empty
            });
        }

        return response;
    }

    public override async Task<DeleteResourcesResponse> DeleteResources(
        DeleteResourcesRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var resourceIds = new List<Guid>();
        foreach (var resourceId in request.ResourceIds)
        {
            if (!Guid.TryParse(resourceId, out var parsedId) || parsedId == Guid.Empty)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resourceId."));
            }

            if (!resourceIds.Contains(parsedId))
            {
                resourceIds.Add(parsedId);
            }
        }

        var deletedCount = 0;
        foreach (var resourceId in resourceIds)
        {
            var result = await _mediator.Send(
                new DeleteResourceCommand(resourceId, userId),
                context.CancellationToken);

            if (result.IsFailure)
            {
                throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
            }

            deletedCount++;
        }

        return new DeleteResourcesResponse
        {
            DeletedCount = deletedCount
        };
    }

    public override async Task<CheckStorageQuotaResponse> CheckStorageQuota(
        CheckStorageQuotaRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        Guid? workspaceId = null;
        if (!string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            if (!Guid.TryParse(request.WorkspaceId, out var parsedWorkspaceId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid workspaceId."));
            }

            if (parsedWorkspaceId != Guid.Empty)
            {
                workspaceId = parsedWorkspaceId;
            }
        }

        var result = await _mediator.Send(
            new CheckStorageQuotaQuery(
                userId,
                request.RequestedBytes,
                string.IsNullOrWhiteSpace(request.Purpose) ? null : request.Purpose.Trim(),
                Math.Max(0, request.EstimatedFileCount),
                workspaceId),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.Internal, result.Error.Description));
        }

        return new CheckStorageQuotaResponse
        {
            Allowed = result.Value.Allowed,
            QuotaBytes = result.Value.QuotaBytes ?? 0,
            UsedBytes = result.Value.UsedBytes,
            ReservedBytes = result.Value.ReservedBytes,
            AvailableBytes = result.Value.AvailableBytes ?? 0,
            MaxUploadFileBytes = result.Value.MaxUploadFileBytes ?? 0,
            SystemStorageQuotaBytes = result.Value.SystemStorageQuotaBytes ?? 0,
            ErrorCode = result.Value.Error?.Code ?? string.Empty,
            ErrorMessage = result.Value.Error?.Description ?? string.Empty
        };
    }

    public override async Task<BackfillResourceProvenanceResponse> BackfillResourceProvenance(
        BackfillResourceProvenanceRequest request,
        ServerCallContext context)
    {
        var items = new List<Application.Resources.Commands.ResourceProvenanceBackfillItem>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (!Guid.TryParse(item.ResourceId, out var resourceId) || resourceId == Guid.Empty)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid resourceId."));
            }

            var originChatSessionId = ParseOptionalGuid(item.OriginChatSessionId, "originChatSessionId");
            if (originChatSessionId.IsFailure)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, originChatSessionId.Error.Description));
            }

            var originChatId = ParseOptionalGuid(item.OriginChatId, "originChatId");
            if (originChatId.IsFailure)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, originChatId.Error.Description));
            }

            items.Add(new Application.Resources.Commands.ResourceProvenanceBackfillItem(
                resourceId,
                string.IsNullOrWhiteSpace(item.OriginKind) ? null : item.OriginKind.Trim(),
                string.IsNullOrWhiteSpace(item.OriginSourceUrl) ? null : item.OriginSourceUrl.Trim(),
                originChatSessionId.Value,
                originChatId.Value));
        }

        var result = await _mediator.Send(
            new BackfillResourceProvenanceCommand(items),
            context.CancellationToken);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.Internal, result.Error.Description));
        }

        return new BackfillResourceProvenanceResponse
        {
            UpdatedCount = result.Value
        };
    }

    public override async Task<GetActiveConfigResponse> GetActiveConfig(
        GetActiveConfigRequest request,
        ServerCallContext context)
    {
        var result = await _mediator.Send(new GetConfigQuery(), context.CancellationToken);

        if (result.IsFailure)
        {
            return new GetActiveConfigResponse
            {
                HasActiveConfig = false
            };
        }

        return new GetActiveConfigResponse
        {
            HasActiveConfig = true,
            ConfigId = result.Value.Id.ToString(),
            ChatModel = result.Value.ChatModel ?? string.Empty,
            MediaAspectRatio = result.Value.MediaAspectRatio ?? string.Empty,
            NumberOfVariances = result.Value.NumberOfVariances ?? 0
        };
    }

    private static GetPublicUserProfileByUsernameResponse MapProfile(Application.Users.Models.PublicUserProfileResponse profile)
    {
        return new GetPublicUserProfileByUsernameResponse
        {
            UserId = profile.Id.ToString(),
            Username = profile.Username,
            FullName = profile.FullName ?? string.Empty,
            AvatarUrl = profile.AvatarPresignedUrl ?? string.Empty
        };
    }

    private static PublicUserProfile MapPublicUserProfile(Application.Users.Models.PublicUserProfileResponse profile)
    {
        return new PublicUserProfile
        {
            UserId = profile.Id.ToString(),
            Username = profile.Username,
            FullName = profile.FullName ?? string.Empty,
            AvatarUrl = profile.AvatarPresignedUrl ?? string.Empty
        };
    }

    private static Result<Guid?> ParseOptionalGuid(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success<Guid?>(null);
        }

        if (!Guid.TryParse(value, out var parsedId))
        {
            return Result.Failure<Guid?>(new SharedLibrary.Common.ResponseModel.Error(
                "Resource.InvalidGuid",
                $"{fieldName} must be a valid GUID."));
        }

        return Result.Success<Guid?>(parsedId == Guid.Empty ? null : parsedId);
    }
}
