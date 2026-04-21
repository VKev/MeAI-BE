using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record DeleteResourceCommand(Guid ResourceId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteResourceCommandHandler : IRequestHandler<DeleteResourceCommand, Result<bool>>
{
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;

    public DeleteResourceCommandHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<bool>> Handle(DeleteResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.ResourceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (resource == null)
        {
            return Result.Failure<bool>(new Error("Resource.NotFound", "Resource not found"));
        }

        // Soft-delete only — keep the file in object storage so existing references
        // from post-builder/product pages (which pin resource ids at create time) keep
        // resolving presigned URLs. The library listing filters IsDeleted so the user
        // sees the item removed there.
        resource.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        resource.IsDeleted = true;
        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(resource);

        return Result.Success(true);
    }
}
