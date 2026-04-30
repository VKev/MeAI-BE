using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Common.Resources;

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
        Guid? workspaceId = null,
        ResourceProvenanceMetadata? provenance = null);

    Task<Result<int>> BackfillResourceProvenanceAsync(
        IReadOnlyList<ResourceProvenanceBackfillRequest> items,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyDictionary<Guid, PublicUserProfileResult>>> GetPublicUserProfilesByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed record UserResourcePresignResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    string? OriginKind = null,
    string? OriginSourceUrl = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);

public sealed record UserResourceCreatedResult(
    Guid ResourceId,
    string PresignedUrl,
    string? ContentType,
    string? ResourceType,
    string? OriginKind = null,
    string? OriginSourceUrl = null,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null);

public sealed record ResourceProvenanceBackfillRequest(
    Guid ResourceId,
    string? OriginKind,
    string? OriginSourceUrl,
    Guid? OriginChatSessionId,
    Guid? OriginChatId);

public sealed record PublicUserProfileResult(
    Guid UserId,
    string Username,
    string? FullName,
    string? AvatarUrl);
