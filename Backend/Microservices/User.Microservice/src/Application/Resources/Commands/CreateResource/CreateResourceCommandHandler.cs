using Application.Resources.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands.CreateResource;

internal sealed class CreateResourceCommandHandler(IResourceRepository resourceRepository)
    : ICommandHandler<CreateResourceCommand, ResourceResponse>
{
    public async Task<Result<ResourceResponse>> Handle(CreateResourceCommand request,
        CancellationToken cancellationToken)
    {
        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Link = request.Link.Trim(),
            Status = request.Status?.Trim(),
            ResourceType = request.ResourceType?.Trim(),
            ContentType = request.ContentType?.Trim(),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await resourceRepository.AddAsync(resource, cancellationToken);

        return Result.Success(ResourceMapping.ToResponse(resource));
    }
}
