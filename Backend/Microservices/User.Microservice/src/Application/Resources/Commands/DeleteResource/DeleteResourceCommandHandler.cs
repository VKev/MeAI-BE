using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands.DeleteResource;

internal sealed class DeleteResourceCommandHandler(IResourceRepository resourceRepository)
    : ICommandHandler<DeleteResourceCommand>
{
    public async Task<Result> Handle(DeleteResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = await resourceRepository.GetByIdForUserAsync(
            request.ResourceId,
            request.UserId,
            cancellationToken);

        if (resource == null)
        {
            return Result.Failure(new Error("Resource.NotFound", "Resource not found"));
        }

        resource.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        resource.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        resourceRepository.Update(resource);

        return Result.Success();
    }
}
