using Application.Abstractions.Data;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record UpdateResourceCommand(
    Guid ResourceId,
    Guid UserId,
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType) : IRequest<Result<ResourceResponse>>;

public sealed class UpdateResourceCommandHandler
    : IRequestHandler<UpdateResourceCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;

    public UpdateResourceCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Resource>();
    }

    public async Task<Result<ResourceResponse>> Handle(UpdateResourceCommand request,
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
            return Result.Failure<ResourceResponse>(new Error("Resource.NotFound", "Resource not found"));
        }

        resource.Link = request.Link.Trim();
        resource.Status = request.Status?.Trim();
        resource.ResourceType = request.ResourceType?.Trim();
        resource.ContentType = request.ContentType?.Trim();
        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _repository.Update(resource);

        return Result.Success(ResourceMapping.ToResponse(resource));
    }
}
