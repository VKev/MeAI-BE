using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Resources;

public interface IUserResourceService
{
    Task<Result<IReadOnlyList<UserResourcePresignResult>>> GetPresignedResourcesAsync(
        Guid userId,
        IReadOnlyList<Guid> resourceIds,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserResourceCreatedResult>>> CreateResourcesFromUrlsAsync(
        Guid userId,
        IReadOnlyList<string> urls,
        string? status,
        string? resourceType,
        CancellationToken cancellationToken,
        Guid? workspaceId = null);

    Task<Result<IReadOnlyDictionary<Guid, PublicUserProfileResult>>> GetPublicUserProfilesByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed record UserResourcePresignResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType);

public sealed record UserResourceCreatedResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType);

public sealed record PublicUserProfileResult(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl);
