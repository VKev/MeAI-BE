using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Resources;

public interface IUserResourceService
{
    Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPresignedResourcesAsync(
        Guid userId,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken cancellationToken);
}

public sealed record UserResourcePresignResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType);
