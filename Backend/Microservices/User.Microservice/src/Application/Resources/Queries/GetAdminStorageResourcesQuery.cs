using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetAdminStorageResourcesQuery(
    Guid? UserId,
    Guid? WorkspaceId,
    string? ResourceType,
    bool IncludeDeleted,
    string? Namespace,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    bool IncludePresignedUrl) : IRequest<Result<IReadOnlyList<AdminStorageResourceResponse>>>;

public sealed class GetAdminStorageResourcesQueryHandler
    : IRequestHandler<GetAdminStorageResourcesQuery, Result<IReadOnlyList<AdminStorageResourceResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public GetAdminStorageResourcesQueryHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService)
    {
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<IReadOnlyList<AdminStorageResourceResponse>>> Handle(
        GetAdminStorageResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = _resourceRepository.GetAll()
            .AsNoTracking();

        if (!request.IncludeDeleted)
        {
            query = query.Where(resource => !resource.IsDeleted);
        }

        if (request.UserId.HasValue)
        {
            query = query.Where(resource => resource.UserId == request.UserId.Value);
        }

        if (request.WorkspaceId.HasValue)
        {
            query = query.Where(resource => resource.WorkspaceId == request.WorkspaceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ResourceType))
        {
            var normalizedResourceType = request.ResourceType.Trim();
            query = query.Where(resource => resource.ResourceType == normalizedResourceType);
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            var storageNamespace = request.Namespace.Trim();
            query = query.Where(resource =>
                resource.StorageNamespace == storageNamespace ||
                (resource.StorageKey != null && EF.Functions.Like(resource.StorageKey, storageNamespace + "/%")) ||
                EF.Functions.Like(resource.Link, "%" + storageNamespace + "/%"));
        }

        if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
        {
            var createdAt = request.CursorCreatedAt.Value;
            var lastId = request.CursorId.Value;
            query = query.Where(resource =>
                (resource.CreatedAt < createdAt) ||
                (resource.CreatedAt == createdAt && resource.Id.CompareTo(lastId) < 0));
        }

        var resources = await query
            .OrderByDescending(resource => resource.CreatedAt)
            .ThenByDescending(resource => resource.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var responses = new List<AdminStorageResourceResponse>(resources.Count);
        foreach (var resource in resources)
        {
            string? presignedUrl = null;
            if (request.IncludePresignedUrl)
            {
                var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
                if (presignedResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<AdminStorageResourceResponse>>(presignedResult.Error);
                }

                presignedUrl = presignedResult.Value;
            }

            responses.Add(new AdminStorageResourceResponse(
                resource.Id,
                resource.UserId,
                resource.WorkspaceId,
                resource.Link,
                presignedUrl,
                resource.Status,
                resource.ResourceType,
                resource.ContentType,
                resource.SizeBytes,
                resource.StorageBucket,
                resource.StorageRegion,
                resource.StorageNamespace,
                string.IsNullOrWhiteSpace(resource.StorageKey) ? resource.Link : resource.StorageKey,
                resource.CreatedAt,
                resource.UpdatedAt,
                resource.DeletedAt,
                resource.ExpiresAt,
                resource.DeletedFromStorageAt));
        }

        return Result.Success<IReadOnlyList<AdminStorageResourceResponse>>(responses);
    }
}
