using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetResourceByIdQuery(Guid ResourceId, Guid UserId) : IRequest<Result<ResourceResponse>>;

public sealed class GetResourceByIdQueryHandler
    : IRequestHandler<GetResourceByIdQuery, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;

    public GetResourceByIdQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<ResourceResponse>> Handle(GetResourceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var resource = await _repository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.ResourceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (resource == null)
        {
            return Result.Failure<ResourceResponse>(new Error("Resource.NotFound", "Resource not found"));
        }

        var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
        if (presignedResult.IsFailure)
        {
            return Result.Failure<ResourceResponse>(presignedResult.Error);
        }

        return Result.Success(ResourceMapping.ToResponse(resource, presignedResult.Value));
    }
}
