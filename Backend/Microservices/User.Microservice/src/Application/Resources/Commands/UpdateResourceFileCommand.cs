using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Application.Resources.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record UpdateResourceFileCommand(
    Guid ResourceId,
    Guid UserId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long ContentLength,
    string? Status,
    string? ResourceType,
    Guid? WorkspaceId = null) : IRequest<Result<ResourceResponse>>;

public sealed class UpdateResourceFileCommandHandler
    : IRequestHandler<UpdateResourceFileCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly IStorageUsageService _storageUsageService;

    public UpdateResourceFileCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        IStorageUsageService storageUsageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
        _storageUsageService = storageUsageService;
    }

    public async Task<Result<ResourceResponse>> Handle(
        UpdateResourceFileCommand request,
        CancellationToken cancellationToken)
    {
        var resource = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.ResourceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (resource == null)
        {
            return Result.Failure<ResourceResponse>(
                new Error("Resource.NotFound", "Resource not found"));
        }

        var storageKey = ResourceStorageKey.Build(request.UserId, resource.Id);
        var requestedDeltaBytes = Math.Max(0L, request.ContentLength - (resource.SizeBytes ?? 0L));
        var quotaResult = await _storageUsageService.EnsureUploadAllowedAsync(
            request.UserId,
            requestedDeltaBytes,
            cancellationToken);
        if (quotaResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(quotaResult.Error);
        }

        await using var fileStream = request.FileStream;
        var uploadResult = await _objectStorageService.UploadAsync(
            new StorageUploadRequest(
                storageKey,
                fileStream,
                request.ContentType,
                request.ContentLength),
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(uploadResult.Error);
        }

        resource.Link = uploadResult.Value.Key;
        resource.StorageProvider = "s3";
        resource.StorageBucket = uploadResult.Value.Bucket;
        resource.StorageRegion = uploadResult.Value.Region;
        resource.StorageNamespace = uploadResult.Value.Namespace;
        resource.StorageKey = uploadResult.Value.Key;
        resource.SizeBytes = request.ContentLength;
        resource.OriginalFileName = request.FileName;
        resource.ContentType = request.ContentType.Trim();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            resource.Status = request.Status.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ResourceType))
        {
            resource.ResourceType = request.ResourceType.Trim();
        }

        if (request.WorkspaceId.HasValue)
        {
            resource.WorkspaceId = request.WorkspaceId.Value;
        }

        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(resource);

        var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        if (presignedResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(presignedResult.Error);
        }

        return Result.Success(ResourceMapping.ToResponse(resource, presignedResult.Value));
    }
}
