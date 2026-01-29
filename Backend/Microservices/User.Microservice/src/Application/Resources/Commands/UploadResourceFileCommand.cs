using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record UploadResourceFileCommand(
    Guid UserId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long ContentLength,
    string? Status,
    string? ResourceType) : IRequest<Result<ResourceResponse>>;

public sealed class UploadResourceFileCommandHandler
    : IRequestHandler<UploadResourceFileCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;

    public UploadResourceFileCommandHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<ResourceResponse>> Handle(
        UploadResourceFileCommand request,
        CancellationToken cancellationToken)
    {
        var resourceId = Guid.CreateVersion7();
        var storageKey = ResourceStorageKey.Build(request.UserId, resourceId);

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

        var resource = new Resource
        {
            Id = resourceId,
            UserId = request.UserId,
            Link = uploadResult.Value.Key,
            Status = request.Status?.Trim(),
            ResourceType = request.ResourceType?.Trim(),
            ContentType = request.ContentType.Trim(),
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
