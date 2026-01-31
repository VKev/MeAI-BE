using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetResourcesByIdsQuery(
    Guid UserId,
    IReadOnlyList<Guid> ResourceIds) : IRequest<Result<List<ResourcePresignResponse>>>;

public sealed class GetResourcesByIdsQueryHandler
    : IRequestHandler<GetResourcesByIdsQuery, Result<List<ResourcePresignResponse>>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;

    public GetResourcesByIdsQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<List<ResourcePresignResponse>>> Handle(
        GetResourcesByIdsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ResourceIds.Count == 0)
        {
            return Result.Failure<List<ResourcePresignResponse>>(
                new Error("Resource.Missing", "At least one resource is required."));
        }

        var uniqueIds = request.ResourceIds.Distinct().ToList();

        var resources = await _repository.GetAll()
            .Where(resource =>
                resource.UserId == request.UserId &&
                uniqueIds.Contains(resource.Id) &&
                !resource.IsDeleted)
            .ToListAsync(cancellationToken);

        if (resources.Count != uniqueIds.Count)
        {
            return Result.Failure<List<ResourcePresignResponse>>(
                new Error("Resource.NotFound", "One or more resources were not found."));
        }

        var resourceLookup = resources.ToDictionary(resource => resource.Id);
        var response = new List<ResourcePresignResponse>(uniqueIds.Count);

        foreach (var resourceId in uniqueIds)
        {
            var resource = resourceLookup[resourceId];
            var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
            if (presignedResult.IsFailure)
            {
                return Result.Failure<List<ResourcePresignResponse>>(presignedResult.Error);
            }

            response.Add(new ResourcePresignResponse(
                resource.Id,
                presignedResult.Value,
                resource.ContentType,
                resource.ResourceType));
        }

        return Result.Success(response);
    }
}
