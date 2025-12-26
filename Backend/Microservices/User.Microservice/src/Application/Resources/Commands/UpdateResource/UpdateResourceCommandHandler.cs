using Application.Resources.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands.UpdateResource;

internal sealed class UpdateResourceCommandHandler(IResourceRepository resourceRepository)
    : ICommandHandler<UpdateResourceCommand, ResourceResponse>
{
    public async Task<Result<ResourceResponse>> Handle(UpdateResourceCommand request,
        CancellationToken cancellationToken)
    {
        var resource = await resourceRepository.GetByIdForUserAsync(
            request.ResourceId,
            request.UserId,
            cancellationToken);

        if (resource == null)
        {
            return Result.Failure<ResourceResponse>(
                new Error("Resource.NotFound", "Resource not found"));
        }

        resource.Link = request.Link.Trim();
        resource.Status = request.Status?.Trim();
        resource.ResourceType = request.ResourceType?.Trim();
        resource.ContentType = request.ContentType?.Trim();
        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        resourceRepository.Update(resource);

        return Result.Success(ResourceMapping.ToResponse(resource));
    }
}
