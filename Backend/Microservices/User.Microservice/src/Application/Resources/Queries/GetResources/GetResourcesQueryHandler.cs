using Application.Resources.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries.GetResources;

internal sealed class GetResourcesQueryHandler(IResourceRepository resourceRepository)
    : IQueryHandler<GetResourcesQuery, IReadOnlyList<ResourceResponse>>
{
    public async Task<Result<IReadOnlyList<ResourceResponse>>> Handle(GetResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var resources = await resourceRepository.GetForUserAsync(request.UserId, cancellationToken);
        var response = resources.Select(ResourceMapping.ToResponse).ToList();
        return Result.Success<IReadOnlyList<ResourceResponse>>(response);
    }
}
