using Application.Resources.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries.GetResourceById;

internal sealed class GetResourceByIdQueryHandler(IResourceRepository resourceRepository)
    : IQueryHandler<GetResourceByIdQuery, ResourceResponse>
{
    public async Task<Result<ResourceResponse>> Handle(GetResourceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var resource = await resourceRepository.GetByIdForUserAsync(
            request.ResourceId,
            request.UserId,
            cancellationToken);

        if (resource == null)
        {
            return Result.Failure<ResourceResponse>(new Error("Resource.NotFound", "Resource not found"));
        }

        return Result.Success(ResourceMapping.ToResponse(resource));
    }
}
