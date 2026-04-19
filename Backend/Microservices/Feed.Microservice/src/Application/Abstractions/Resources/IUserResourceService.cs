using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Resources;

public interface IUserResourceService
{
    Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPresignedResourcesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> resourceIds,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPublicPresignedResourcesAsync(
        IReadOnlyCollection<Guid> resourceIds,
        CancellationToken cancellationToken);

    Task<Result<PublicUserProfileResult>> GetPublicUserProfileByUsernameAsync(
        string username,
        CancellationToken cancellationToken);
}

public sealed record UserResourcePresignResult(
    Guid ResourceId,
    string PresignedUrl,
    string ContentType,
    string ResourceType);

public sealed record PublicUserProfileResult(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl);

