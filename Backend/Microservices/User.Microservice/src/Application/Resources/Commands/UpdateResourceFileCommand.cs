using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
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
    string? ResourceType) : IRequest<Result<ResourceResponse>>;

public sealed class UpdateResourceFileCommandHandler
    : IRequestHandler<UpdateResourceFileCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;

    public UpdateResourceFileCommandHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
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
        resource.ContentType = request.ContentType.Trim();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            resource.Status = request.Status.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ResourceType))
        {
            resource.ResourceType = request.ResourceType.Trim();
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
