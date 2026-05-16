using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Commands;

public sealed record HardDeleteResourcesCommand(
    Guid UserId,
    IReadOnlyCollection<Guid> ResourceIds) : IRequest<Result<int>>;

public sealed class HardDeleteResourcesCommandHandler
    : IRequestHandler<HardDeleteResourcesCommand, Result<int>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<HardDeleteResourcesCommandHandler> _logger;

    public HardDeleteResourcesCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        ILogger<HardDeleteResourcesCommandHandler> logger)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(
        HardDeleteResourcesCommand request,
        CancellationToken cancellationToken)
    {
        var resourceIds = request.ResourceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (resourceIds.Count == 0)
        {
            return Result.Success(0);
        }

        var resources = await _repository.GetAll()
            .Where(resource =>
                resource.UserId == request.UserId &&
                resourceIds.Contains(resource.Id))
            .ToListAsync(cancellationToken);

        foreach (var resource in resources)
        {
            var storageKey = ResolveStorageKey(resource);
            if (string.IsNullOrWhiteSpace(storageKey) || resource.DeletedFromStorageAt.HasValue)
            {
                continue;
            }

            var deleteResult = await _objectStorageService.DeleteAsync(storageKey, cancellationToken);
            if (deleteResult.IsFailure)
            {
                _logger.LogWarning(
                    "Hard resource delete could not remove object storage file. ResourceId: {ResourceId}, UserId: {UserId}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
                    resource.Id,
                    request.UserId,
                    deleteResult.Error.Code,
                    deleteResult.Error.Description);
            }
        }

        _repository.DeleteRange(resources);
        return Result.Success(resources.Count);
    }

    private static string? ResolveStorageKey(Resource resource) =>
        string.IsNullOrWhiteSpace(resource.StorageKey)
            ? resource.Link
            : resource.StorageKey;
}
