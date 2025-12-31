using Application.Abstractions.Data;
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

    public GetResourceByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Resource>();
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

        return Result.Success(ResourceMapping.ToResponse(resource));
    }
}
