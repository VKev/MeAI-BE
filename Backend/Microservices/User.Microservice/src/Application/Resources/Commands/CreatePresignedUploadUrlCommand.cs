using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Application.Resources.Services;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Common.Resources;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record CreatePresignedUploadUrlCommand(
    Guid UserId,
    string FileName,
    string ContentType,
    long ContentLength,
    string? ResourceType,
    Guid? WorkspaceId = null) : IRequest<Result<PresignedUploadResponse>>;

public sealed class CreatePresignedUploadUrlCommandHandler
    : IRequestHandler<CreatePresignedUploadUrlCommand, Result<PresignedUploadResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly IStorageUsageService _storageUsageService;

    public CreatePresignedUploadUrlCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        IStorageUsageService storageUsageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
        _storageUsageService = storageUsageService;
    }

    public async Task<Result<PresignedUploadResponse>> Handle(
        CreatePresignedUploadUrlCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Result.Failure<PresignedUploadResponse>(new Error("Resource.FileNameRequired", "File name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ContentType))
        {
            return Result.Failure<PresignedUploadResponse>(new Error("Resource.ContentTypeRequired", "Content type is required."));
        }

        if (request.ContentLength <= 0)
        {
            return Result.Failure<PresignedUploadResponse>(new Error("Resource.InvalidContentLength", "File size must be greater than zero."));
        }

        // 1. Validate quota and subscription plan file limits
        var quotaResult = await _storageUsageService.EnsureUploadAllowedAsync(
            request.UserId,
            request.ContentLength,
            cancellationToken);

        if (quotaResult.IsFailure)
        {
            return Result.Failure<PresignedUploadResponse>(quotaResult.Error);
        }

        // 2. Generate ResourceId and S3 storage key path
        var resourceId = Guid.CreateVersion7();
        var storageKey = ResourceStorageKey.Build(request.UserId, resourceId);

        // 3. Generate S3 presigned PUT URL
        // Using a default of 30 minutes for upload expiration to give ample time for large uploads
        var ttl = TimeSpan.FromMinutes(30);
        var presignResult = _objectStorageService.GetPresignedUploadUrl(storageKey, request.ContentType, ttl);
        if (presignResult.IsFailure)
        {
            return Result.Failure<PresignedUploadResponse>(presignResult.Error);
        }

        var result = presignResult.Value;

        // 4. Save placeholder resource in PostgreSQL
        var resource = new Resource
        {
            Id = resourceId,
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            Link = result.Key,
            StorageProvider = "s3",
            StorageBucket = result.Bucket,
            StorageRegion = result.Region,
            StorageNamespace = result.Namespace,
            StorageKey = result.Key,
            SizeBytes = request.ContentLength,
            OriginalFileName = request.FileName.Trim(),
            Status = "PendingUpload",
            ResourceType = request.ResourceType?.Trim(),
            ContentType = request.ContentType.Trim(),
            OriginKind = ResourceOriginKinds.UserUpload,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(resource, cancellationToken);

        // 5. Prepare response headers required for PUT request
        var headers = new Dictionary<string, string>
        {
            { "Content-Type", request.ContentType }
        };

        return Result.Success(new PresignedUploadResponse(
            resourceId,
            result.Url,
            result.Key,
            "PUT",
            headers));
    }
}
