using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Application.Resources.Services;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record CompletePresignedUploadCommand(
    Guid ResourceId,
    Guid UserId,
    string? Status = null) : IRequest<Result<ResourceResponse>>;

public sealed class CompletePresignedUploadCommandHandler
    : IRequestHandler<CompletePresignedUploadCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly IStorageUsageService _storageUsageService;

    public CompletePresignedUploadCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        IStorageUsageService storageUsageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
        _storageUsageService = storageUsageService;
    }

    public async Task<Result<ResourceResponse>> Handle(
        CompletePresignedUploadCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Retrieve the resource
        var resource = await _repository.GetByIdAsync(request.ResourceId, cancellationToken);
        if (resource == null || resource.IsDeleted || resource.UserId != request.UserId)
        {
            return Result.Failure<ResourceResponse>(
                new Error("Resource.NotFound", "Resource not found or unauthorized access."));
        }

        // 2. If it is already completed, return successful response (idempotency)
        if (resource.Status != "PendingUpload")
        {
            var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
            if (presignedResult.IsFailure)
            {
                return Result.Failure<ResourceResponse>(presignedResult.Error);
            }

            return Result.Success(ResourceMapping.ToResponse(resource, presignedResult.Value));
        }

        // 3. Verify S3 file existence and retrieve actual file size
        if (string.IsNullOrWhiteSpace(resource.StorageKey))
        {
            return Result.Failure<ResourceResponse>(
                new Error("Resource.InvalidStorageKey", "Storage key is missing on the resource."));
        }

        var objectInfoResult = await _objectStorageService.GetObjectInfoAsync(resource.StorageKey, cancellationToken);
        if (objectInfoResult.IsFailure)
        {
            if (objectInfoResult.Error.Code == "S3.ObjectNotFound")
            {
                return Result.Failure<ResourceResponse>(
                    new Error("Resource.UploadNotFinished", "The file has not been uploaded to S3 yet."));
            }

            return Result.Failure<ResourceResponse>(objectInfoResult.Error);
        }

        var actualSize = objectInfoResult.Value.SizeBytes;
        var originalSize = resource.SizeBytes ?? 0;

        // 4. Validate quota delta if the actual file size is larger than registered
        if (actualSize != originalSize)
        {
            var sizeDelta = actualSize - originalSize;
            if (sizeDelta > 0)
            {
                var quotaResult = await _storageUsageService.EnsureUploadAllowedAsync(
                    request.UserId,
                    sizeDelta,
                    cancellationToken);

                if (quotaResult.IsFailure)
                {
                    // Clean up: Delete S3 object and remove placeholder DB row to prevent quota bypass
                    await _objectStorageService.DeleteAsync(resource.StorageKey, cancellationToken);
                    _repository.Delete(resource);

                    return Result.Failure<ResourceResponse>(quotaResult.Error);
                }
            }

            resource.SizeBytes = actualSize;
        }

        // 5. Update resource status to complete upload
        resource.Status = request.Status?.Trim();
        if (resource.Status == "PendingUpload")
        {
            // If client sends "PendingUpload" again, reset to null/Active state
            resource.Status = null;
        }

        resource.LastVerifiedAt = DateTimeExtensions.PostgreSqlUtcNow;
        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _repository.Update(resource);

        // 6. Generate the download presigned URL and return mapping response
        var readPresignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        if (readPresignedResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(readPresignedResult.Error);
        }

        return Result.Success(ResourceMapping.ToResponse(resource, readPresignedResult.Value));
    }
}
