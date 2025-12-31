using Application.Abstractions.Data;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record CreateResourceCommand(
    Guid UserId,
    string Link,
    string? Status,
    string? ResourceType,
    string? ContentType) : IRequest<Result<ResourceResponse>>;

public sealed class CreateResourceCommandHandler
    : IRequestHandler<CreateResourceCommand, Result<ResourceResponse>>
{
    private readonly IRepository<Resource> _repository;

    public CreateResourceCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Resource>();
    }

    public async Task<Result<ResourceResponse>> Handle(CreateResourceCommand request,
        CancellationToken cancellationToken)
    {
        var resource = new Resource
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Link = request.Link.Trim(),
            Status = request.Status?.Trim(),
            ResourceType = request.ResourceType?.Trim(),
            ContentType = request.ContentType?.Trim(),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(resource, cancellationToken);

        return Result.Success(ResourceMapping.ToResponse(resource));
    }
}
