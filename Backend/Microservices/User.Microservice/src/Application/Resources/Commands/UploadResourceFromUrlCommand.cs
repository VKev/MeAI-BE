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

public sealed record UploadResourceFromUrlCommand(
    Guid UserId,
    string Url,
    string? Status,
    string? ResourceType,
    Guid? WorkspaceId = null,
    ResourceProvenanceMetadata? Provenance = null) : IRequest<Result<ResourceResponse>>;

public sealed class UploadResourceFromUrlCommandHandler
    : IRequestHandler<UploadResourceFromUrlCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly IRemoteFileService _remoteFileService;
    private readonly IStorageUsageService _storageUsageService;

    public UploadResourceFromUrlCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        IRemoteFileService remoteFileService,
        IStorageUsageService storageUsageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
        _remoteFileService = remoteFileService;
        _storageUsageService = storageUsageService;
    }

    public async Task<Result<ResourceResponse>> Handle(
        UploadResourceFromUrlCommand request,
        CancellationToken cancellationToken)
    {
        var fetchResult = await _remoteFileService.FetchAsync(request.Url, cancellationToken);
        if (fetchResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(fetchResult.Error);
        }

        var resourceId = Guid.CreateVersion7();
        var storageKey = ResourceStorageKey.Build(request.UserId, resourceId);
        var quotaResult = await _storageUsageService.EnsureUploadAllowedAsync(
            request.UserId,
            fetchResult.Value.ContentLength,
            cancellationToken);
        if (quotaResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(quotaResult.Error);
        }

        await using var contentStream = fetchResult.Value.Content;
        var uploadResult = await _objectStorageService.UploadAsync(
            new StorageUploadRequest(
                storageKey,
                contentStream,
                fetchResult.Value.ContentType,
                fetchResult.Value.ContentLength),
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(uploadResult.Error);
        }

        var resource = new Resource
        {
            Id = resourceId,
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            Link = uploadResult.Value.Key,
            StorageProvider = "s3",
            StorageBucket = uploadResult.Value.Bucket,
            StorageRegion = uploadResult.Value.Region,
            StorageNamespace = uploadResult.Value.Namespace,
            StorageKey = uploadResult.Value.Key,
            SizeBytes = fetchResult.Value.ContentLength,
            Status = request.Status?.Trim(),
            ResourceType = request.ResourceType?.Trim(),
            ContentType = fetchResult.Value.ContentType.Trim(),
            OriginKind = Normalize(request.Provenance?.OriginKind),
            OriginSourceUrl = Normalize(request.Provenance?.OriginSourceUrl) ?? request.Url.Trim(),
            OriginChatSessionId = NormalizeGuid(request.Provenance?.OriginChatSessionId),
            OriginChatId = NormalizeGuid(request.Provenance?.OriginChatId),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(resource, cancellationToken);

        var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        if (presignedResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(presignedResult.Error);
        }

        return Result.Success(ResourceMapping.ToResponse(resource, presignedResult.Value));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Guid? NormalizeGuid(Guid? value)
    {
        return value is null || value == Guid.Empty ? null : value;
    }
}
