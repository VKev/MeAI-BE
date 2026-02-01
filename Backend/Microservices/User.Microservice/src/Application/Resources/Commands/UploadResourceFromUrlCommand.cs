using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record UploadResourceFromUrlCommand(
    Guid UserId,
    string Url,
    string? Status,
    string? ResourceType) : IRequest<Result<ResourceResponse>>;

public sealed class UploadResourceFromUrlCommandHandler
    : IRequestHandler<UploadResourceFromUrlCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly IRemoteFileService _remoteFileService;

    public UploadResourceFromUrlCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        IRemoteFileService remoteFileService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
        _remoteFileService = remoteFileService;
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
            Link = uploadResult.Value.Key,
            Status = request.Status?.Trim(),
            ResourceType = request.ResourceType?.Trim(),
            ContentType = fetchResult.Value.ContentType.Trim(),
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
}
