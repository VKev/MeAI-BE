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

    Task<Result<IReadOnlyDictionary<Guid, PublicUserProfileResult>>> GetPublicUserProfilesByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed record UserResourcePresignResult(
    Guid ResourceId,
    string PresignedUrl,
    string ContentType,
    string ResourceType,
    string? OriginKind = null,
    string? OriginSourceUrl = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);

public sealed record PublicUserProfileResult(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl);
